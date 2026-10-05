namespace Floatie.Core;

/// <summary>One fence: a floating panel showing the files of a folder that match its filter.</summary>
public sealed class FenceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New fence";

    /// <summary>A real path, or a known-folder token: <c>::Desktop</c>, <c>::Downloads</c>, <c>::Documents</c>.</summary>
    public string Folder { get; set; } = KnownFolders.DesktopToken;

    /// <summary>Semicolon-separated patterns, e.g. "*.pdf;*.docx". "*" shows everything; "!*.tmp" excludes.</summary>
    public string Patterns { get; set; } = "*";

    public bool ShowFolders { get; set; } = true;
    public SortOrder Sort { get; set; } = SortOrder.Name;
    public string Accent { get; set; } = "#3DBFA7";

    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 300;
    public bool RolledUp { get; set; }
    public bool Hidden { get; set; }
}

public enum SortOrder { Name, Newest, Type, Size }

/// <summary>Moves loose files of one folder into category subfolders ("Tidy").</summary>
public sealed class TidyRule
{
    public string Category { get; set; } = "";
    public string Patterns { get; set; } = "";
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public List<FenceConfig> Fences { get; set; } = new();
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 10;
    public bool HideDesktopIcons { get; set; }
    public string TidyFolder { get; set; } = KnownFolders.DownloadsToken;
    public List<TidyRule> TidyRules { get; set; } = FileOrganizer.DefaultRules();
}
