using System.Text.Json;
using System.Text.Json.Serialization;

namespace Floatie.Core;

/// <summary>Settings live in %AppData%\Floatie\settings.json. Writes are atomic (temp file +
/// replace) so a crash mid-save can't lose your layout, and a corrupt file is kept aside as
/// settings.broken.json instead of being silently overwritten.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, "settings.json");
    public string TidyLogPath => Path.Combine(Folder, "last-tidy.json");

    public SettingsStore(string? folder = null)
    {
        Folder = folder ?? Environment.GetEnvironmentVariable("FLOATIE_HOME")
                 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Floatie");
    }

    public bool Exists => File.Exists(FilePath);

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
            if (settings is not null) return settings;
        }
        catch (JsonException) { }
        File.Copy(FilePath, Path.Combine(Folder, "settings.broken.json"), overwrite: true);
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>The fences a first run starts with, stacked down the right edge of the work area.</summary>
    public static List<FenceConfig> DefaultFences(double workLeft, double workTop, double workWidth, double workHeight)
    {
        const double w = 380, gap = 14;
        double x = workLeft + workWidth - w - 24, y = workTop + 24;
        double h = Math.Max(170, (workHeight - 48 - 3 * gap) / 4);
        FenceConfig fence(string title, string folder, string patterns, string accent, bool folders, SortOrder sort)
        {
            var f = new FenceConfig { Title = title, Folder = folder, Patterns = patterns, Accent = accent,
                                      ShowFolders = folders, Sort = sort, Left = x, Top = y, Width = w, Height = h };
            y += h + gap;
            return f;
        }
        return new()
        {
            fence("Apps & shortcuts", KnownFolders.DesktopToken, FilterPresets.All[6].Patterns, "#5AB3F2", false, SortOrder.Name),
            fence("Documents", KnownFolders.DesktopToken, FilterPresets.All[1].Patterns, "#3DBFA7", false, SortOrder.Newest),
            fence("Images & media", KnownFolders.DesktopToken,
                  FilterPresets.All[2].Patterns + ";" + FilterPresets.All[3].Patterns + ";" + FilterPresets.All[4].Patterns,
                  "#F2A541", false, SortOrder.Newest),
            fence("Downloads", KnownFolders.DownloadsToken, "*", "#9A8CF2", true, SortOrder.Newest),
        };
    }
}
