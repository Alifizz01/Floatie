using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Floatie.Native;

/// <summary>The Win32 plumbing that makes a window behave like part of the desktop.</summary>
public static class Desktop
{
    private const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    private const int WM_WINDOWPOSCHANGING = 0x46, WM_COMMAND = 0x111;
    private const uint SWP_NOZORDER = 0x4;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    /// <summary>Keep `window` on the desktop: out of the taskbar and Alt-Tab, under every
    /// application window, and still visible after Win+D (owned by the desktop's Progman
    /// window, which "show desktop" never hides).</summary>
    public static void Pin(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero) SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);
        HwndSource.FromHwnd(hwnd)?.AddHook(StayAtBottom);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);   // NOSIZE | NOMOVE | NOACTIVATE
    }

    private static IntPtr StayAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            // Whatever asked to raise us (a click, activation), keep the z-order at the bottom.
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Show or hide Windows' own desktop icons (the same switch as right-click
    /// desktop > View > Show desktop icons), so fences can be the whole desktop.</summary>
    public static bool SetIconsVisible(bool visible)
    {
        var defView = FindDefView();
        if (defView == IntPtr.Zero) return false;
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        bool current = listView != IntPtr.Zero && IsWindowVisible(listView);
        if (current != visible) SendMessage(defView, WM_COMMAND, new IntPtr(0x7402), IntPtr.Zero);
        return true;
    }

    private static IntPtr FindDefView()
    {
        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        // With a wallpaper slideshow Windows moves the icons under a WorkerW sibling.
        for (var worker = IntPtr.Zero; defView == IntPtr.Zero;)
        {
            worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
            if (worker == IntPtr.Zero) break;
            defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return defView;
    }

    /// <summary>Use Windows' dark title bar on a dialog so it matches the fences.</summary>
    public static void DarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    // ----------------------------------------------------------------- start with Windows
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Floatie") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Floatie", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Floatie", throwOnMissingValue: false);
        }
    }

    /// <summary>False when autostart points at an exe that no longer exists (moved or updated).</summary>
    public static bool StartupTargetExists
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Floatie") is string cmd && File.Exists(cmd.Trim('"'));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
}
