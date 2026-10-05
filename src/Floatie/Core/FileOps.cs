using Microsoft.VisualBasic.FileIO;

namespace Floatie.Core;

/// <summary>File operations, all of them non-destructive: nothing is ever overwritten
/// (a free name like "report (2).pdf" is chosen instead) and deletes go to the Recycle Bin.</summary>
public static class FileOps
{
    /// <summary>"name.ext" in folder, or "name (2).ext", "name (3).ext", ... whichever is free.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int n = 2; ; n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Move (or copy) a file or folder into `targetFolder`. Returns the new path,
    /// or null when it is already there. Works across drives.</summary>
    public static string? Transfer(string source, string targetFolder, bool copy)
    {
        var parent = Path.GetDirectoryName(source.TrimEnd('\\', '/'));
        if (!copy && string.Equals(parent, targetFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return null;
        var dest = UniquePath(targetFolder, Path.GetFileName(source.TrimEnd('\\', '/')));
        bool isDir = Directory.Exists(source);
        if (isDir)
        {
            if (copy) FileSystem.CopyDirectory(source, dest);
            else FileSystem.MoveDirectory(source, dest);
        }
        else
        {
            if (copy) File.Copy(source, dest);
            else FileSystem.MoveFile(source, dest);
        }
        return dest;
    }

    public static void Recycle(string path)
    {
        if (Directory.Exists(path))
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    /// <summary>Rename in place. Throws on an invalid or taken name rather than overwriting.</summary>
    public static string Rename(string path, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"\"{newName}\" is not a valid file name.");
        var dest = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (string.Equals(dest, path, StringComparison.Ordinal)) return path;
        bool caseOnly = string.Equals(dest, path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(dest) || Directory.Exists(dest)))
            throw new IOException($"\"{newName}\" already exists here.");
        if (Directory.Exists(path)) Directory.Move(path, dest);
        else File.Move(path, dest);
        return dest;
    }
}
