using System.Runtime.InteropServices;

namespace Floatie.Core;

/// <summary>Resolves the folder tokens a fence can point at. The Desktop is two folders on
/// Windows (yours and the Public one, which holds most installed apps' shortcuts), so a
/// Desktop fence shows both, exactly like the real desktop does.</summary>
public static class KnownFolders
{
    public const string DesktopToken = "::Desktop";
    public const string DownloadsToken = "::Downloads";
    public const string DocumentsToken = "::Documents";

    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public static string Downloads
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) == 0)
                    return p;
            }
            catch (Exception) { /* non-Windows test host: fall through */ }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>All folders whose files the fence lists.</summary>
    public static IReadOnlyList<string> Sources(string folder) => folder switch
    {
        DesktopToken => new[] { UserDesktop, PublicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        DownloadsToken => new[] { Downloads },
        DocumentsToken => new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        _ => new[] { folder },
    };

    /// <summary>Where files dropped onto the fence go: the first source (your own Desktop, not Public).</summary>
    public static string Target(string folder) => Sources(folder).FirstOrDefault() ?? folder;

    public static string DisplayName(string folder) => folder switch
    {
        DesktopToken => "Desktop",
        DownloadsToken => "Downloads",
        DocumentsToken => "Documents",
        _ => Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : folder,
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags,
                                                   IntPtr token, out string path);
}
