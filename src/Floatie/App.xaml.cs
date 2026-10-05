using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Floatie.Core;
using Floatie.Native;
using Floatie.Views;
using Forms = System.Windows.Forms;

namespace Floatie;

/// <summary>Floatie has no main window: it lives in the tray and owns one window per fence.</summary>
public partial class App : Application
{
    private const string InstanceName = "Floatie.SingleInstance.v1";
    private Mutex? _mutex;
    private EventWaitHandle? _wakeup;
    private Forms.NotifyIcon? _tray;
    private readonly List<FenceWindow> _windows = new();
    private bool _allHidden;

    public SettingsStore Store { get; } = new();
    public AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, InstanceName, out bool first);
        _wakeup = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".wake");
        if (!first)
        {
            _wakeup.Set();                 // ask the running copy to bring its fences back
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_wakeup, (_, _) => Dispatcher.BeginInvoke(() => ShowAll(true)), null, -1, false);
        DispatcherUnhandledException += OnUnhandled;
        SessionEnding += (_, _) => SaveLayout();           // shutdown / sign-out: keep every position
        // A handful of small panels: drawing them on the CPU is cheaper than waking the GPU and
        // copying each layered window back from it on every repaint.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((w, _) => { if (w is not FenceWindow) Desktop.DarkTitleBar((Window)w); }));

        bool firstRun = !Store.Exists;
        Settings = Store.Load();
        if (firstRun)
        {
            var area = SystemParameters.WorkArea;
            Settings.Fences = SettingsStore.DefaultFences(area.Left, area.Top, area.Width, area.Height);
            Save();
            if (Environment.GetEnvironmentVariable("FLOATIE_HOME") is null) Desktop.StartsWithWindows = true;
        }
        else if (Desktop.StartsWithWindows && !Desktop.StartupTargetExists)
            Desktop.StartsWithWindows = true;                // the exe was moved: point autostart at it again
        foreach (var fence in Settings.Fences.Where(f => !f.Hidden)) Open(fence);
        if (Settings.HideDesktopIcons) Desktop.SetIconsVisible(false);

        CreateTray();
        WatchExplorer();
        TrimMemoryLater();
        if (firstRun)
            _tray!.ShowBalloonTip(6000, "Floatie is on your desktop",
                "Your desktop files are sorted into fences. Right-click a fence or this tray icon for options.",
                Forms.ToolTipIcon.None);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveLayout();
        if (Settings.HideDesktopIcons) Desktop.SetIconsVisible(true);   // never leave the desktop empty
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Floatie", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;          // one failed action shouldn't take every fence down with it
    }

    private void SaveLayout()
    {
        foreach (var w in _windows) w.RememberBounds();
        Save();
    }

    public void Save()
    {
        try { Store.Save(Settings); }
        catch (IOException) { /* retried on the next change */ }
    }

    // ------------------------------------------------------------------ fences

    private FenceWindow Open(FenceConfig fence)
    {
        var window = new FenceWindow(this, fence);
        window.Closed += (_, _) => _windows.Remove(window);
        _windows.Add(window);
        window.Show();
        return window;
    }

    public void NewFence()
    {
        var area = SystemParameters.WorkArea;
        var fence = new FenceConfig
        {
            Left = area.Left + (area.Width - 380) / 2,
            Top = area.Top + (area.Height - 300) / 2,
            Accent = FenceEditorWindow.Accents[Settings.Fences.Count % FenceEditorWindow.Accents.Length],
        };
        if (new FenceEditorWindow(fence, isNew: true).ShowDialog() != true) return;
        Settings.Fences.Add(fence);
        Save();
        Open(fence).Activate();
    }

    public void EditFence(FenceWindow window)
    {
        var editor = new FenceEditorWindow(window.Config, isNew: false);
        if (editor.ShowDialog() != true) return;
        if (editor.DeleteRequested)
        {
            Settings.Fences.Remove(window.Config);
            window.Close();
        }
        else window.ApplyConfig();
        Save();
    }

    public void HideFence(FenceWindow window)
    {
        window.Config.Hidden = true;
        window.Close();
        Save();
        _tray?.ShowBalloonTip(3000, "Fence hidden", $"Bring \"{window.Config.Title}\" back from the tray icon.", Forms.ToolTipIcon.None);
    }

    private void Unhide(FenceConfig fence)
    {
        fence.Hidden = false;
        Save();
        Open(fence);
    }

    public void Tidy(string folder)
    {
        Settings.TidyFolder = folder;
        Save();
        new TidyWindow(this).Show();
    }

    private void ShowAll(bool show)
    {
        _allHidden = !show;
        foreach (var w in _windows) w.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        if (!show) TrimMemoryLater();
    }

    /// <summary>When Explorer restarts (crash, update) the desktop window our fences belong to is
    /// destroyed and takes them with it. Windows announces the new shell with "TaskbarCreated";
    /// rebuild every fence then.</summary>
    private void WatchExplorer()
    {
        int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var listener = new HwndSource(new HwndSourceParameters("Floatie.ShellListener") { Width = 0, Height = 0, WindowStyle = 0 });
        listener.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == taskbarCreated)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };   // let the new desktop settle
                timer.Tick += (_, _) => { timer.Stop(); ReopenFences(); };
                timer.Start();
            }
            return IntPtr.Zero;
        });
    }

    private void ReopenFences()
    {
        SaveLayout();
        foreach (var w in _windows.ToList()) w.Close();
        foreach (var fence in Settings.Fences.Where(f => !f.Hidden)) Open(fence);
        if (Settings.HideDesktopIcons) Desktop.SetIconsVisible(false);
        if (_allHidden) ShowAll(false);
    }

    /// <summary>Start-up (parsing XAML, first icon loads) leaves garbage and touched pages behind
    /// that an idle desktop tool has no use for: collect once and hand the pages back to Windows.</summary>
    private void TrimMemoryLater()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        };
        timer.Start();
    }

    // ------------------------------------------------------------------ tray

    private void CreateTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/floatie.ico")).Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Floatie",
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Right) ShowTrayMenu(); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowAll(_allHidden); };
    }

    /// <summary>The tray menu uses the same dark WPF style as the fences, not a WinForms strip.</summary>
    private void ShowTrayMenu()
    {
        var menu = new ContextMenu();
        Item(menu, "New fence…", NewFence);
        Item(menu, _allHidden ? "Show fences" : "Hide fences", () => ShowAll(_allHidden));
        var hidden = Settings.Fences.Where(f => f.Hidden).ToList();
        if (hidden.Count > 0)
        {
            var sub = new MenuItem { Header = "Hidden fences" };
            foreach (var f in hidden) Item(sub, f.Title, () => Unhide(f));
            menu.Items.Add(sub);
        }
        Item(menu, "Tidy a folder…", () => Tidy(Settings.TidyFolder));
        menu.Items.Add(new Separator());
        Check(menu, "Hide Windows desktop icons", Settings.HideDesktopIcons, on =>
        {
            Settings.HideDesktopIcons = on;
            Desktop.SetIconsVisible(!on);
        });
        Check(menu, "Snap to grid", Settings.SnapToGrid, on => Settings.SnapToGrid = on);
        Check(menu, "Start with Windows", Desktop.StartsWithWindows, on => Desktop.StartsWithWindows = on);
        Item(menu, "Open settings folder", () =>
        {
            Directory.CreateDirectory(Store.Folder);
            Process.Start(new ProcessStartInfo(Store.Folder) { UseShellExecute = true });
        });
        menu.Items.Add(new Separator());
        Item(menu, "Exit Floatie", Shutdown);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        // Without a foreground window the menu would never close when you click elsewhere.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource src) SetForegroundWindow(src.Handle);
        };
        menu.IsOpen = true;
    }

    private static void Item(ItemsControl menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void Check(ItemsControl menu, string header, bool value, Action<bool> set)
    {
        var item = new MenuItem { Header = header, IsChecked = value };
        item.Click += (_, _) => { set(!value); Save(); };
        menu.Items.Add(item);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
