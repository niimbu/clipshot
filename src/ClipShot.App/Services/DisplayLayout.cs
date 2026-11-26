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
    private const int WmNcCalcSize = 0x0083;
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorDpiTypeEffective = 0;
    private static readonly IntPtr HwndTopMost = new(-1);

    public static void Attach(Window window, DrawingRectangle pixels)
    {
        window.WindowState = WindowState.Normal;
        // Size against the target monitor DPI, not the window's current screen.
        // Mixed-DPI setups otherwise leave the secondary display too small or too large.
        GetMonitorDpi(pixels, out var dpiX, out var dpiY);
        SyncDipSize(window, pixels, dpiX, dpiY);

        void Apply() => Lock(window, pixels);

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
            Apply();
        else
            window.SourceInitialized += (_, _) => Apply();

        window.DpiChanged += (_, e) =>
            SyncDipSize(window, pixels, e.NewDpi.PixelsPerInchX, e.NewDpi.PixelsPerInchY);
    }

    public static void GetMonitorDpi(DrawingRectangle pixels, out double dpiX, out double dpiY)
    {
        var rect = new NativeRect
        {
            Left = pixels.Left,
            Top = pixels.Top,
            Right = pixels.Right,
            Bottom = pixels.Bottom
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var x, out var y) == 0 &&
            x > 0 && y > 0)
        {
            dpiX = x;
            dpiY = y;
            return;
        }

        dpiX = 96;
        dpiY = 96;
    }

    public static double ToDip(int pixels, double dpi) =>
        pixels * 96d / Math.Max(1, dpi);

    private static void Lock(Window window, DrawingRectangle pixels)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(Hook);
            IntPtr Hook(IntPtr hookHwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg == WmNcCalcSize && wParam != IntPtr.Zero)
                {
                    // Borderless windows still keep a thin frame. That frame pushed the
                    // capture a little above the region the pointer selected.
                    handled = true;
                    return IntPtr.Zero;
                }

                if (msg == WmWindowPosChanging)
                    ForceBounds(lParam, pixels);
                handled = false;
                return IntPtr.Zero;
            }
        }

        GetMonitorDpi(pixels, out var dpiX, out var dpiY);
        SyncDipSize(window, pixels, dpiX, dpiY);
        SetWindowPos(hwnd, HwndTopMost, pixels.X, pixels.Y, pixels.Width, pixels.Height, 0);
        // SetWindowPos can change the effective DPI; sync again after the move.
        GetMonitorDpi(pixels, out dpiX, out dpiY);
        SyncDipSize(window, pixels, dpiX, dpiY);
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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
