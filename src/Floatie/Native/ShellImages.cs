using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floatie.Native;

/// <summary>What Explorer shows for a file: the app's own icon for .exe/.lnk, the type icon for
/// documents, a real thumbnail for pictures. Comes from IShellItemImageFactory, converted
/// pixel-by-pixel so alpha survives (the usual HBITMAP route turns transparency black).</summary>
public static class ShellImages
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PerFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".appref-ms",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff",
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".pdf",
    };

    /// <summary>Cached by type for plain documents, by file (and its timestamp) for anything
    /// whose picture depends on the file itself.</summary>
    public static ImageSource? Get(string path, bool isFolder, DateTime modified, int size = 48)
    {
        var ext = isFolder ? "<folder>" : Path.GetExtension(path);
        var key = isFolder || PerFileTypes.Contains(ext) || ext.Length == 0
            ? $"{path}|{modified.Ticks}|{size}"
            : $"{ext}|{size}";
        return Cache.GetOrAdd(key, _ => Load(path, size));
    }

    private static ImageSource? Load(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0) return null;
            try
            {
                if (factory.GetImage(new SIZE(size, size), SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk, out hbmp) != 0)
                    return null;
            }
            finally { Marshal.ReleaseComObject(factory); }
            return ToBitmapSource(hbmp);
        }
        catch (COMException) { return null; }
        finally
        {
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
        }
    }

    private static BitmapSource? ToBitmapSource(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmBits == IntPtr.Zero) return null;
        int stride = bm.bmWidthBytes, height = Math.Abs(bm.bmHeight);
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.bmBits, pixels, 0, pixels.Length);
        if (bm.bmHeight > 0)                      // bottom-up DIB: flip rows
        {
            var flipped = new byte[pixels.Length];
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(pixels, row * stride, flipped, (height - 1 - row) * stride, stride);
            pixels = flipped;
        }
        var format = bm.bmBitsPixel == 32 ? PixelFormats.Pbgra32 : PixelFormats.Bgr24;
        var source = BitmapSource.Create(bm.bmWidth, height, 96, 96, format, null, pixels, stride);
        source.Freeze();
        return source;
    }

    [Flags]
    private enum SIIGBF { ResizeToFit = 0x00, BiggerSizeOk = 0x01, IconOnly = 0x04 }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE(int cx, int cy)
    {
        public readonly int cx = cx;
        public readonly int cy = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}
