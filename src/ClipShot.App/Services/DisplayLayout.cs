using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ClipShot.Services;

/// <summary>
/// Pins a borderless window to physical monitor pixels. WPF's maximized state
/// only covers the primary display, and DIP coordinates drift on mixed-DPI setups.
/// </summary>
internal static class DisplayLayout
{
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private static readonly IntPtr HwndTopMost = new(-1);

    public static void Attach(Window window, DrawingRectangle pixels)
    {
        window.WindowState = WindowState.Normal;
        window.SourceInitialized += (_, _) => Lock(window, pixels);
        window.DpiChanged += (_, e) => SyncDipSize(window, pixels, e.NewDpi.PixelsPerInchX, e.NewDpi.PixelsPerInchY);
    }

    private static void Lock(Window window, DrawingRectangle pixels)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook((_, msg, _, lParam, ref bool handled) =>
            {
                if (msg == WmWindowPosChanging)
                    ForceBounds(lParam, pixels);
                handled = false;
                return IntPtr.Zero;
            });
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        SyncDipSize(window, pixels, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
        SetWindowPos(hwnd, HwndTopMost, pixels.X, pixels.Y, pixels.Width, pixels.Height, 0);
    }

    private static void SyncDipSize(Window window, DrawingRectangle pixels, double dpiX, double dpiY)
    {
        var scaleX = Math.Max(1, dpiX) / 96d;
        var scaleY = Math.Max(1, dpiY) / 96d;
        window.Left = pixels.Left / scaleX;
        window.Top = pixels.Top / scaleY;
        window.Width = Math.Max(1, pixels.Width / scaleX);
        window.Height = Math.Max(1, pixels.Height / scaleY);
    }

    private static void ForceBounds(IntPtr lParam, DrawingRectangle pixels)
    {
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        pos.x = pixels.X;
        pos.y = pixels.Y;
        pos.cx = pixels.Width;
        pos.cy = pixels.Height;
        pos.flags &= ~SwpNoSize;
        pos.flags &= ~SwpNoMove;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }
}
