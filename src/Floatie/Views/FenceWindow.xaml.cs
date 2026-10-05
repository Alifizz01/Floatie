using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Floatie.Core;
using Floatie.Native;

namespace Floatie.Views;

/// <summary>One tile in a fence: the file plus its (lazily loaded) shell icon.</summary>
public sealed class Tile(FileEntry entry) : INotifyPropertyChanged
{
    private ImageSource? _icon;
    public FileEntry Entry { get; } = entry;
    public string Name => Entry.DisplayName;
    public string Tooltip => Entry.IsFolder
        ? $"{Entry.Name}\nFolder · {Entry.Modified:g}"
        : $"{Entry.Name}\n{Size(Entry.Size)} · {Entry.Modified:g}";
    public ImageSource? Icon { get => _icon; set { _icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    private static string Size(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1048576.0:0.#} MB",
        _ => $"{b / 1073741824.0:0.##} GB",
    };
}

/// <summary>A live view of a folder sitting on the desktop: filtered, sorted, watched for
/// changes, and behaving like Explorer for open / rename / delete / drag in and out.</summary>
public partial class FenceWindow : Window
{
    private const double HeaderHeight = 40;
    private readonly App _app;
    private readonly ObservableCollection<Tile> _tiles = new();
    private readonly ICollectionView _view;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _reloadTimer, _saveTimer;
    private Point? _dragStart;
    private bool _draggingOut, _loading;
    private int _generation;

    public FenceConfig Config { get; }

    public FenceWindow(App app, FenceConfig config)
    {
        InitializeComponent();
        _app = app;
        Config = config;
        _view = CollectionViewSource.GetDefaultView(_tiles);
        _view.Filter = o => SearchBox.Text.Length == 0
            || ((Tile)o).Entry.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);
        Items.ItemsSource = _view;
        ((INotifyCollectionChanged)_view).CollectionChanged += (_, _) => UpdateCounts();

        // Coalesce bursts (a download writing, a folder copy) into one re-list.
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); RememberBounds(); _app.Save(); };

        Width = config.Width;
        Height = config.RolledUp ? HeaderHeight : config.Height;
        (Left, Top) = OnScreen(config.Left, config.Top, config.Width);
        SourceInitialized += (_, _) => Desktop.Pin(this);
        LocationChanged += (_, _) => QueueSave();
        SizeChanged += (_, _) => QueueSave();
        PreviewKeyDown += OnWindowKeyDown;
        Closed += (_, _) => StopWatching();
        ApplyConfig();
    }

    /// <summary>Re-read the fence settings (after the editor) and rebuild everything.</summary>
    public void ApplyConfig()
    {
        TitleText.Text = Config.Title;
        Title = $"Floatie · {Config.Title}";
        var accent = BrushFrom(Config.Accent);
        AccentBar.Background = accent;
        DropHint.BorderBrush = accent;
        DropHint.Background = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.09 };
        ApplyRoll();
        Watch();
        Reload();
    }

    /// <summary>The saved spot, unless a monitor has gone since: then pull the fence back so
    /// at least its title bar can be grabbed.</summary>
    private static (double, double) OnScreen(double left, double top, double width)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        bool visible = left + width > vl + 60 && left < vr - 60 && top >= vt - 10 && top < vb - HeaderHeight;
        if (visible) return (left, top);
        var work = SystemParameters.WorkArea;
        return (Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width)),
                Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - HeaderHeight)));
    }

    private static SolidColorBrush BrushFrom(string hex)
    {
        try { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; }
        catch (FormatException) { return (SolidColorBrush)Application.Current.Resources["Accent"]; }
    }

    // ------------------------------------------------------------------ contents

    private void Watch()
    {
        StopWatching();
        foreach (var folder in KnownFolders.Sources(Config.Folder).Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(QueueReload);
            w.Created += changed; w.Deleted += changed; w.Changed += changed;
            w.Renamed += (_, _) => Dispatcher.BeginInvoke(QueueReload);
            w.Error += (_, _) => Dispatcher.BeginInvoke(QueueReload);   // buffer overflow: just re-list
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }

    private void StopWatching()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private void QueueReload() { _reloadTimer.Stop(); _reloadTimer.Start(); }

    /// <summary>List the folder off the UI thread, then diff into the tile list so icons and
    /// selection survive a refresh.</summary>
    public async void Reload()
    {
        var gen = ++_generation;
        var config = Config;
        List<FileEntry> entries;
        _loading = true;
        try { entries = await Task.Run(() => FolderLister.List(config)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = new();
        }
        if (gen != _generation) return;                 // a newer reload already ran
        _loading = false;

        var old = _tiles.ToDictionary(t => t.Entry.Path, StringComparer.OrdinalIgnoreCase);
        var selected = Items.SelectedItems.Cast<Tile>().Select(t => t.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = entries.Select(e => old.TryGetValue(e.Path, out var t) && t.Entry == e ? t : new Tile(e)).ToList();

        _tiles.Clear();
        foreach (var t in fresh) _tiles.Add(t);
        foreach (var t in fresh.Where(t => selected.Contains(t.Entry.Path))) Items.SelectedItems.Add(t);
        UpdateCounts();

        foreach (var t in fresh.Where(t => t.Icon is null))
        {
            var e = t.Entry;
            t.Icon = await Task.Run(() => ShellImages.Get(e.Path, e.IsFolder, e.Modified));
            if (gen != _generation) return;
        }
    }

    private void UpdateCounts()
    {
        int shown = _view.Cast<object>().Count();
        CountText.Text = SearchBox.Text.Length > 0 ? $"{shown} of {_tiles.Count}" : $"{_tiles.Count}";
        EmptyText.Text = _loading || shown > 0 ? ""
            : SearchBox.Text.Length > 0 ? $"Nothing matches \"{SearchBox.Text}\"."
            : !KnownFolders.Sources(Config.Folder).Any(Directory.Exists) ? "This fence's folder doesn't exist any more.\nRight-click the title > Edit fence."
            : $"Nothing here yet.\nDrop files to move them to {KnownFolders.DisplayName(Config.Folder)}.";
    }

    private IEnumerable<FileEntry> Selection => Items.SelectedItems.Cast<Tile>().Select(t => t.Entry).ToList();

    // ------------------------------------------------------------------ header, move, roll up

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleRoll(); return; }
        DragMove();
        if (_app.Settings.SnapToGrid) Snap();
        QueueSave();
    }

    private void Snap()
    {
        double g = Math.Max(1, _app.Settings.GridSize);
        Left = Math.Round(Left / g) * g;
        Top = Math.Round(Top / g) * g;
        if (!Config.RolledUp)
        {
            Width = Math.Max(MinWidth, Math.Round(Width / g) * g);
            Height = Math.Max(MinHeight, Math.Round(Height / g) * g);
        }
    }

    private void ToggleRoll()
    {
        RememberBounds();
        Config.RolledUp = !Config.RolledUp;
        Height = Config.RolledUp ? HeaderHeight : Config.Height;
        ApplyRoll();
        _app.Save();
    }

    private void ApplyRoll()
    {
        Body.Visibility = Config.RolledUp ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = Config.RolledUp ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        RollButton.Content = Config.RolledUp ? "" : "";
        RollButton.ToolTip = Config.RolledUp ? "Roll down" : "Roll up (double-click the title)";
    }

    private void OnRollClick(object sender, RoutedEventArgs e) => ToggleRoll();

    private void QueueSave() { if (IsLoaded) { _saveTimer.Stop(); _saveTimer.Start(); } }

    public void RememberBounds()
    {
        Config.Left = Left; Config.Top = Top; Config.Width = Width;
        if (!Config.RolledUp && ActualHeight > HeaderHeight + 1) Config.Height = Height;
    }

    // ------------------------------------------------------------------ search

    private void OnSearchClick(object sender, RoutedEventArgs e) => ShowSearch();

    private void ShowSearch()
    {
        if (Config.RolledUp) ToggleRoll();
        SearchBox.Visibility = Visibility.Visible;
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void HideSearch()
    {
        SearchBox.Text = "";
        SearchBox.Visibility = Visibility.Collapsed;
        Items.Focus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) { _view.Refresh(); UpdateCounts(); }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideSearch(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Enter && Items.Items.Count > 0)
        {
            Items.SelectedIndex = 0;
            (Items.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ keyboard

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ShowSearch(); e.Handled = true; }
        else if (e.Key == Key.F5) { Reload(); e.Handled = true; }
    }

    private void OnItemsKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Enter: OpenSelection(); break;
            case Key.Delete: RecycleSelection(); break;
            case Key.F2: RenameSelected(); break;
            case Key.C when ctrl: CopyToClipboard(); break;
            case Key.Escape when SearchBox.Visibility == Visibility.Visible: HideSearch(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ file actions

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is not null) OpenSelection();
    }

    private void OpenSelection()
    {
        foreach (var f in Selection.Take(15)) Shell(f.Path);
    }

    private static void Shell(string file, string? args = null)
    {
        try
        {
            if (args is null) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Cancelled "how do you want to open this" dialog, or a broken shortcut.
            if (ex is Win32Exception { NativeErrorCode: 1223 }) return;
            MessageBox.Show(ex.Message, "Floatie", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RecycleSelection()
    {
        foreach (var f in Selection)
        {
            try { FileOps.Recycle(f.Path); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Couldn't delete {f.Name}:\n{ex.Message}", "Floatie", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        QueueReload();
    }

    private void RenameSelected()
    {
        if (Selection.FirstOrDefault() is not { } f) return;
        var dialog = new InputDialog("Rename", "New name", f.Name, selectStem: !f.IsFolder) { Owner = this };
        while (dialog.ShowDialog() == true)
        {
            try { FileOps.Rename(f.Path, dialog.Value); QueueReload(); return; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                MessageBox.Show(ex.Message, "Can't rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                dialog = new InputDialog("Rename", "New name", dialog.Value, selectStem: !f.IsFolder) { Owner = this };
            }
        }
    }

    private void CopyToClipboard()
    {
        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange(Selection.Select(f => f.Path).ToArray());
        if (files.Count > 0) Clipboard.SetFileDropList(files);
    }

    // ------------------------------------------------------------------ context menus

    private void OnItemsMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ItemMenu.Items.Clear();
        if (ItemUnder(e.OriginalSource) is null)
        {
            Items.SelectedItems.Clear();
            FillFenceMenu(ItemMenu);
            return;
        }
        var sel = Selection.ToList();
        var first = sel[0];
        Add(ItemMenu, "Open", OpenSelection, "Enter");
        if (sel.Count == 1 && !first.IsFolder)
            Add(ItemMenu, "Open with…", () => Shell("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {first.Path}"));
        Add(ItemMenu, "Show in Explorer", () => Shell("explorer.exe", $"/select,\"{first.Path}\""));
        ItemMenu.Items.Add(new Separator());
        Add(ItemMenu, "Copy", CopyToClipboard, "Ctrl+C");
        Add(ItemMenu, sel.Count == 1 ? "Copy path" : "Copy paths",
            () => Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(f => f.Path))));
        if (sel.Count == 1) Add(ItemMenu, "Rename", RenameSelected, "F2");
        ItemMenu.Items.Add(new Separator());
        Add(ItemMenu, sel.Count == 1 ? "Delete" : $"Delete {sel.Count} items", RecycleSelection, "Del");
    }

    private void OnFenceMenuOpening(object sender, ContextMenuEventArgs e)
    {
        FenceMenu.Items.Clear();
        FillFenceMenu(FenceMenu);
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        FenceMenu.Items.Clear();
        FillFenceMenu(FenceMenu);
        FenceMenu.PlacementTarget = (UIElement)sender;
        FenceMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        FenceMenu.IsOpen = true;
    }

    private void FillFenceMenu(ContextMenu menu)
    {
        var sort = new MenuItem { Header = "Sort by" };
        foreach (var order in Enum.GetValues<SortOrder>())
        {
            var item = new MenuItem { Header = order.ToString(), IsChecked = Config.Sort == order };
            item.Click += (_, _) => { Config.Sort = order; _app.Save(); Reload(); };
            sort.Items.Add(item);
        }
        menu.Items.Add(sort);
        Add(menu, "Filter", ShowSearch, "Ctrl+F");
        Add(menu, "Refresh", Reload, "F5");
        menu.Items.Add(new Separator());
        Add(menu, "Edit fence…", () => _app.EditFence(this));
        Add(menu, $"Open {KnownFolders.DisplayName(Config.Folder)}", () => Shell(KnownFolders.Target(Config.Folder)));
        Add(menu, $"Tidy {KnownFolders.DisplayName(Config.Folder)}…", () => _app.Tidy(Config.Folder));
        Add(menu, Config.RolledUp ? "Roll down" : "Roll up", ToggleRoll);
        Add(menu, "Hide fence", () => _app.HideFence(this));
        menu.Items.Add(new Separator());
        Add(menu, "New fence…", () => _app.NewFence());
    }

    private static void Add(ItemsControl menu, string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private ListBoxItem? ItemUnder(object source)
    {
        for (var d = source as DependencyObject; d is not null && d != Items; d = VisualTreeHelper.GetParent(d))
            if (d is ListBoxItem item) return item;
        return null;
    }

    // ------------------------------------------------------------------ drag out

    private void OnItemsMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = ItemUnder(e.OriginalSource) is not null ? e.GetPosition(Items) : null;
    }

    private void OnItemsMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(Items) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        var paths = Selection.Select(f => f.Path).ToArray();
        if (paths.Length == 0) return;
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(Items, new DataObject(DataFormats.FileDrop, paths),
                                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally { _draggingOut = false; }
        QueueReload();
    }

    // ------------------------------------------------------------------ drop in

    private DragDropEffects DropEffect(DragEventArgs e)
    {
        if (_draggingOut || Config.RolledUp || !e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
        return (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropEffect(e);
        e.Handled = true;
        bool on = e.Effects != DragDropEffects.None;
        DropHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        DropText.Text = $"{(e.Effects == DragDropEffects.Copy ? "Copy" : "Move")} to {KnownFolders.DisplayName(Config.Folder)}";
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        var effect = DropEffect(e);
        if (effect == DragDropEffects.None || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Effects = effect;
        var target = KnownFolders.Target(Config.Folder);
        var failed = new List<string>();
        foreach (var f in files)
        {
            try { FileOps.Transfer(f, target, copy: effect == DragDropEffects.Copy); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        if (failed.Count > 0)
            MessageBox.Show(string.Join("\n", failed), "Some files weren't moved", MessageBoxButton.OK, MessageBoxImage.Warning);
        QueueReload();
    }
}
