using System.Text.Json;

namespace Floatie.Core;

public sealed record MoveOp(string Source, string Destination, string Category);

/// <summary>"Tidy": sort the loose files of one folder (Downloads by default) into category
/// subfolders. Always planned first and shown for approval, never overwrites, skips files
/// that are still downloading, and can be undone, even after a restart.</summary>
public static class FileOrganizer
{
    /// <summary>Unfinished downloads and files touched this recently are left alone.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);
    private static readonly PatternMatcher InProgress = new("*.crdownload;*.part;*.partial;*.download;*.tmp;~$*");

    public static List<TidyRule> DefaultRules() => new()
    {
        new() { Category = "Documents", Patterns = FilterPresets.All[1].Patterns },
        new() { Category = "Images", Patterns = FilterPresets.All[2].Patterns },
        new() { Category = "Videos", Patterns = FilterPresets.All[3].Patterns },
        new() { Category = "Music", Patterns = FilterPresets.All[4].Patterns },
        new() { Category = "Archives", Patterns = FilterPresets.All[5].Patterns },
        new() { Category = "Installers", Patterns = "*.exe;*.msi;*.msix;*.appx;*.appinstaller" },
        new() { Category = "Code", Patterns = FilterPresets.All[7].Patterns },
    };

    /// <summary>What would move where. Only top-level files; folders are never touched.</summary>
    public static List<MoveOp> Plan(string folder, IReadOnlyList<TidyRule> rules, DateTime? now = null)
    {
        var plan = new List<MoveOp>();
        if (!Directory.Exists(folder)) return plan;
        var cutoff = (now ?? DateTime.Now) - SettleTime;
        var matchers = rules.Select(r => (r.Category, Matcher: new PatternMatcher(r.Patterns))).ToList();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in new DirectoryInfo(folder).EnumerateFiles().OrderBy(f => f.Name))
        {
            if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            if (InProgress.IsMatch(file.Name) || file.LastWriteTime > cutoff) continue;
            var rule = matchers.FirstOrDefault(m => m.Matcher.IsMatch(file.Name));
            if (rule.Category is null) continue;
            var targetDir = Path.Combine(folder, rule.Category);
            var dest = UniqueAmong(targetDir, file.Name, reserved);
            reserved.Add(dest);
            plan.Add(new MoveOp(file.FullName, dest, rule.Category));
        }
        return plan;
    }

    /// <summary>Carry out a plan. Returns the operations that really happened (the undo log).
    /// A file that can't be moved (open in another app) is skipped, so the log always matches
    /// what is on disk.</summary>
    public static List<MoveOp> Apply(IEnumerable<MoveOp> plan)
    {
        var done = new List<MoveOp>();
        foreach (var op in plan)
        {
            if (!File.Exists(op.Source)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(op.Destination)!);
                var dest = File.Exists(op.Destination)
                    ? FileOps.UniquePath(Path.GetDirectoryName(op.Destination)!, Path.GetFileName(op.Destination))
                    : op.Destination;
                File.Move(op.Source, dest);
                done.Add(op with { Destination = dest });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return done;
    }

    /// <summary>Move everything back. Files moved or renamed since are skipped, never clobbered.</summary>
    public static int Undo(IEnumerable<MoveOp> log)
    {
        int restored = 0;
        foreach (var op in log.Reverse())
        {
            if (!File.Exists(op.Destination) || File.Exists(op.Source)) continue;
            File.Move(op.Destination, op.Source);
            restored++;
            var dir = Path.GetDirectoryName(op.Destination)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);                     // remove category folders Tidy created
        }
        return restored;
    }

    public static void SaveLog(string path, List<MoveOp> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(log));
    }

    public static List<MoveOp> LoadLog(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<MoveOp>>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    private static string UniqueAmong(string folder, string name, HashSet<string> reserved)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !reserved.Contains(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int n = 2; ; n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!File.Exists(path) && !reserved.Contains(path)) return path;
        }
    }
}
