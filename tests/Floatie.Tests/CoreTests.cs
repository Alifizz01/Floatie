using Floatie.Core;
using Xunit;

namespace Floatie.Tests;

/// <summary>A throw-away folder per test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floatie-test-" + Guid.NewGuid().ToString("N")[..8]);
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name, DateTime? modified = null, string content = "x")
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        System.IO.File.SetLastWriteTime(p, modified ?? DateTime.Now.AddHours(-1));
        return p;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class PatternMatcherTests
{
    [Theory]
    [InlineData("*", "anything.bin", true)]
    [InlineData("*.pdf;*.docx", "Report.PDF", true)]
    [InlineData("*.pdf;*.docx", "report.pdf.lnk", false)]
    [InlineData("report*.xlsx", "Report Q3.xlsx", true)]
    [InlineData("*.doc", "a.docx", false)]
    [InlineData("*.doc*", "a.docx", true)]
    [InlineData("*;!*.tmp", "draft.tmp", false)]
    [InlineData("!*.tmp", "notes.txt", true)]          // only excludes: everything else is in
    [InlineData("*.pdf; *.txt", "notes.txt", true)]    // spaces around separators are fine
    [InlineData("My File?.txt", "My File2.txt", true)] // spaces inside a pattern are part of it
    [InlineData("a+b(1).txt", "a+b(1).txt", true)]     // regex characters are literal
    [InlineData("", "x.y", true)]
    public void Matches(string patterns, string name, bool expected) =>
        Assert.Equal(expected, new PatternMatcher(patterns).IsMatch(name));

    [Fact]
    public void Presets_are_disjoint_where_they_should_be()
    {
        var docs = new PatternMatcher(FilterPresets.All[1].Patterns);
        var images = new PatternMatcher(FilterPresets.All[2].Patterns);
        Assert.True(docs.IsMatch("thesis.docx"));
        Assert.False(images.IsMatch("thesis.docx"));
        Assert.True(images.IsMatch("photo.JPG"));
    }
}

public class FolderListerTests
{
    [Fact]
    public void Lists_matching_files_folders_first_and_hides_hidden()
    {
        using var d = new TempDir();
        d.File("b.pdf"); d.File("a.pdf"); d.File("c.txt");
        Directory.CreateDirectory(System.IO.Path.Combine(d.Path, "Zfolder"));
        var hidden = d.File("secret.pdf");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var names = FolderLister.List(new[] { d.Path }, "*.pdf", showFolders: true, SortOrder.Name).Select(e => e.Name);
        Assert.Equal(new[] { "Zfolder", "a.pdf", "b.pdf" }, names);

        var noFolders = FolderLister.List(new[] { d.Path }, "*", showFolders: false, SortOrder.Name).Select(e => e.Name);
        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.txt" }, noFolders);
    }

    [Fact]
    public void Sorts_by_newest_type_and_size()
    {
        var t = DateTime.Now;
        var e = new[]
        {
            new FileEntry(@"C:\x\old.txt", "old.txt", false, t.AddDays(-2), 10),
            new FileEntry(@"C:\x\new.pdf", "new.pdf", false, t, 5),
            new FileEntry(@"C:\x\mid.doc", "mid.doc", false, t.AddDays(-1), 99),
        };
        Assert.Equal(new[] { "new.pdf", "mid.doc", "old.txt" }, FolderLister.Sort(e, SortOrder.Newest).Select(x => x.Name));
        Assert.Equal(new[] { "mid.doc", "new.pdf", "old.txt" }, FolderLister.Sort(e, SortOrder.Type).Select(x => x.Name));
        Assert.Equal(new[] { "mid.doc", "old.txt", "new.pdf" }, FolderLister.Sort(e, SortOrder.Size).Select(x => x.Name));
    }

    [Fact]
    public void Missing_folder_lists_nothing() =>
        Assert.Empty(FolderLister.List(new[] { @"C:\does\not\exist\floatie" }, "*", true, SortOrder.Name));

    [Fact]
    public void Shortcut_extensions_are_hidden_like_explorer()
    {
        Assert.Equal("Steam", new FileEntry(@"C:\d\Steam.lnk", "Steam.lnk", false, default, 0).DisplayName);
        Assert.Equal("a.pdf", new FileEntry(@"C:\d\a.pdf", "a.pdf", false, default, 0).DisplayName);
    }
}

public class FileOpsTests
{
    [Fact]
    public void UniquePath_never_reuses_a_taken_name()
    {
        using var d = new TempDir();
        Assert.Equal(System.IO.Path.Combine(d.Path, "r.pdf"), FileOps.UniquePath(d.Path, "r.pdf"));
        d.File("r.pdf"); d.File("r (2).pdf");
        Assert.Equal(System.IO.Path.Combine(d.Path, "r (3).pdf"), FileOps.UniquePath(d.Path, "r.pdf"));
    }

    [Fact]
    public void Transfer_moves_without_overwriting()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        var a = src.File("a.txt", content: "new");
        dst.File("a.txt", content: "old");
        var moved = FileOps.Transfer(a, dst.Path, copy: false);
        Assert.Equal(System.IO.Path.Combine(dst.Path, "a (2).txt"), moved);
        Assert.False(File.Exists(a));
        Assert.Equal("old", File.ReadAllText(System.IO.Path.Combine(dst.Path, "a.txt")));
        Assert.Equal("new", File.ReadAllText(moved!));
    }

    [Fact]
    public void Transfer_copy_keeps_source_and_same_folder_move_is_a_noop()
    {
        using var d = new TempDir();
        var a = d.File("a.txt");
        Assert.Null(FileOps.Transfer(a, d.Path, copy: false));
        var copy = FileOps.Transfer(a, d.Path, copy: true);
        Assert.True(File.Exists(a));
        Assert.Equal(System.IO.Path.Combine(d.Path, "a (2).txt"), copy);
    }

    [Fact]
    public void Rename_refuses_bad_or_taken_names_but_allows_case_change()
    {
        using var d = new TempDir();
        var a = d.File("a.txt");
        d.File("b.txt");
        Assert.Throws<IOException>(() => FileOps.Rename(a, "b.txt"));
        Assert.Throws<ArgumentException>(() => FileOps.Rename(a, "bad:name.txt"));
        Assert.Throws<ArgumentException>(() => FileOps.Rename(a, "  "));
        var renamed = FileOps.Rename(a, "A.txt");
        Assert.Equal("A.txt", new DirectoryInfo(d.Path).GetFiles("a.txt")[0].Name);
        Assert.Equal(System.IO.Path.Combine(d.Path, "A.txt"), renamed);
    }
}

public class FileOrganizerTests
{
    [Fact]
    public void Plans_moves_by_category_and_skips_unsafe_files()
    {
        using var d = new TempDir();
        var now = DateTime.Now;
        d.File("thesis.pdf"); d.File("photo.jpg"); d.File("setup.exe"); d.File("unknown.xyz");
        d.File("movie.mp4.crdownload");               // still downloading
        d.File("fresh.pdf", modified: now);           // touched just now
        File.SetAttributes(d.File("hidden.pdf"), FileAttributes.Hidden);
        d.File(@"Documents\already.pdf");             // in a subfolder: untouched

        var plan = FileOrganizer.Plan(d.Path, FileOrganizer.DefaultRules(), now);
        var map = plan.ToDictionary(p => System.IO.Path.GetFileName(p.Source), p => p.Category);
        Assert.Equal(new Dictionary<string, string>
        {
            ["photo.jpg"] = "Images", ["setup.exe"] = "Installers", ["thesis.pdf"] = "Documents",
        }, map);
    }

    [Fact]
    public void Apply_never_overwrites_and_undo_restores_everything()
    {
        using var d = new TempDir();
        d.File("a.pdf", content: "loose");
        d.File(@"Documents\a.pdf", content: "filed earlier");
        d.File("b.jpg");

        var plan = FileOrganizer.Plan(d.Path, FileOrganizer.DefaultRules());
        Assert.Equal(System.IO.Path.Combine(d.Path, "Documents", "a (2).pdf"), plan.Single(p => p.Category == "Documents").Destination);

        var log = FileOrganizer.Apply(plan);
        Assert.Equal(2, log.Count);
        Assert.Equal("filed earlier", File.ReadAllText(System.IO.Path.Combine(d.Path, "Documents", "a.pdf")));
        Assert.Equal("loose", File.ReadAllText(System.IO.Path.Combine(d.Path, "Documents", "a (2).pdf")));

        var logFile = System.IO.Path.Combine(d.Path, "log.json");
        FileOrganizer.SaveLog(logFile, log);
        Assert.Equal(2, FileOrganizer.Undo(FileOrganizer.LoadLog(logFile)));
        Assert.Equal("loose", File.ReadAllText(System.IO.Path.Combine(d.Path, "a.pdf")));
        Assert.True(File.Exists(System.IO.Path.Combine(d.Path, "b.jpg")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(d.Path, "Images")));    // created by tidy, now empty: removed
        Assert.True(Directory.Exists(System.IO.Path.Combine(d.Path, "Documents"))); // held a file before: kept
    }

    [Fact]
    public void Undo_skips_files_that_were_replaced_since()
    {
        using var d = new TempDir();
        d.File("a.pdf", content: "original");
        var log = FileOrganizer.Apply(FileOrganizer.Plan(d.Path, FileOrganizer.DefaultRules()));
        d.File("a.pdf", content: "a new file with the same name");
        Assert.Equal(0, FileOrganizer.Undo(log));
        Assert.Equal("a new file with the same name", File.ReadAllText(System.IO.Path.Combine(d.Path, "a.pdf")));
        Assert.Equal("original", File.ReadAllText(System.IO.Path.Combine(d.Path, "Documents", "a.pdf")));
    }

    [Fact]
    public void Missing_or_corrupt_log_means_nothing_to_undo()
    {
        using var d = new TempDir();
        Assert.Empty(FileOrganizer.LoadLog(System.IO.Path.Combine(d.Path, "none.json")));
        Assert.Empty(FileOrganizer.LoadLog(d.File("bad.json", content: "{not json")));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_settings()
    {
        using var d = new TempDir();
        var store = new SettingsStore(d.Path);
        Assert.False(store.Exists);
        var s = new AppSettings { SnapToGrid = false, Fences = { new FenceConfig { Title = "Work", Sort = SortOrder.Size, Left = 12.5 } } };
        store.Save(s);
        var back = store.Load();
        Assert.False(back.SnapToGrid);
        Assert.Equal("Work", back.Fences[0].Title);
        Assert.Equal(SortOrder.Size, back.Fences[0].Sort);
        Assert.Equal(12.5, back.Fences[0].Left);
        Assert.Contains("\"Size\"", File.ReadAllText(store.FilePath));   // enums stored readably
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_and_defaults_load()
    {
        using var d = new TempDir();
        var store = new SettingsStore(d.Path);
        d.File("settings.json", content: "{ broken");
        var s = store.Load();
        Assert.Empty(s.Fences);
        Assert.Equal("{ broken", File.ReadAllText(System.IO.Path.Combine(d.Path, "settings.broken.json")));
    }

    [Fact]
    public void Default_fences_fit_the_work_area()
    {
        var fences = SettingsStore.DefaultFences(0, 0, 1920, 1040);
        Assert.Equal(4, fences.Count);
        Assert.All(fences, f =>
        {
            Assert.InRange(f.Left, 0, 1920 - f.Width);
            Assert.InRange(f.Top + f.Height, 0, 1040);
        });
        Assert.Equal(fences.Count, fences.Select(f => f.Id).Distinct().Count());
    }
}

public class FileOrganizerLockTests
{
    [Fact]
    public void Locked_file_is_skipped_and_the_rest_still_move()
    {
        using var d = new TempDir();
        var locked = d.File("a.pdf");
        d.File("b.pdf");
        var plan = FileOrganizer.Plan(d.Path, FileOrganizer.DefaultRules());
        List<MoveOp> log;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            log = FileOrganizer.Apply(plan);
        Assert.Equal("b.pdf", System.IO.Path.GetFileName(Assert.Single(log).Source));
        Assert.True(File.Exists(locked));
    }
}
