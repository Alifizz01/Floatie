namespace Floatie.Core;

public sealed record FileEntry(string Path, string Name, bool IsFolder, DateTime Modified, long Size)
{
    public string Extension => IsFolder ? "" : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>Explorer hides the extension of shortcuts; so do we.</summary>
    public string DisplayName => Extension is ".lnk" or ".url" or ".appref-ms" ? System.IO.Path.GetFileNameWithoutExtension(Name) : Name;
}

/// <summary>Lists what a fence shows: the matching, visible entries of its source folders.</summary>
public static class FolderLister
{
    public static List<FileEntry> List(FenceConfig fence) =>
        List(KnownFolders.Sources(fence.Folder), fence.Patterns, fence.ShowFolders, fence.Sort);

    public static List<FileEntry> List(IEnumerable<string> folders, string patterns, bool showFolders, SortOrder sort)
    {
        var matcher = new PatternMatcher(patterns);
        var entries = new List<FileEntry>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            var dir = new DirectoryInfo(folder);
            IEnumerable<FileSystemInfo> items;
            try { items = dir.EnumerateFileSystemInfos(); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var item in items)
            {
                try
                {
                    // What Explorer hides by default, we hide too: hidden/system files, desktop.ini.
                    if ((item.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    bool isFolder = (item.Attributes & FileAttributes.Directory) != 0;
                    if (isFolder ? !showFolders : !matcher.IsMatch(item.Name)) continue;
                    long size = item is FileInfo f ? f.Length : 0;
                    entries.Add(new FileEntry(item.FullName, item.Name, isFolder, item.LastWriteTime, size));
                }
                catch (IOException) { /* vanished while listing */ }
            }
        }
        return Sort(entries, sort);
    }

    public static List<FileEntry> Sort(IEnumerable<FileEntry> entries, SortOrder sort)
    {
        // Folders first, like Explorer, then the chosen order.
        var folders = entries.Where(e => e.IsFolder);
        var files = entries.Where(e => !e.IsFolder);
        IEnumerable<FileEntry> ordered(IEnumerable<FileEntry> s) => sort switch
        {
            SortOrder.Newest => s.OrderByDescending(e => e.Modified),
            SortOrder.Type => s.OrderBy(e => e.Extension).ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            SortOrder.Size => s.OrderByDescending(e => e.Size),
            _ => s.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        };
        return ordered(folders).Concat(ordered(files)).ToList();
    }
}
