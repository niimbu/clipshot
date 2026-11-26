using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipShot.Services;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace ClipShot;

internal readonly record struct NormalizedRect(double Left, double Top, double Width, double Height);

internal sealed class RegionPick
{
    public required BitmapSource Image { get; init; }
    public BitmapSource? Background { get; init; }
    public NormalizedRect Fractions { get; init; }
    public required DrawingRectangle Cover { get; init; }
    public bool InPlace { get; init; }
}

/// <summary>
/// One overlay per display. The drag is stored in virtual-screen pixels so it can
/// cross monitors, then cropped from the stitched desktop capture.
/// </summary>
internal sealed class RegionCaptureSession
{
    private readonly DesktopCapture _desktop;
    private readonly double _dimOpacity;
    private readonly List<RegionSelectionWindow> _windows = new();
    private DispatcherFrame? _frame;
    private DrawingPoint _anchor;
    private bool _dragging;
    private bool _done;
    private RegionPick? _pick;

    public RegionCaptureSession(DesktopCapture desktop, double dimOpacity)
    {
        _desktop = desktop;
        _dimOpacity = dimOpacity;
    }

    public bool TrySelect(out RegionPick? pick)
    {
        pick = null;
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
            return false;

        foreach (var screen in screens.OrderBy(screen => screen.Primary ? 0 : 1))
        {
            var image = WithMonitorDpi(Crop(_desktop.Image, _desktop.Bounds, screen.Bounds), screen.Bounds);
            _windows.Add(new RegionSelectionWindow(this, screen, image, _dimOpacity));
        }

        try
        {
            // Do not use ShowDialog on the primary only — that leaves the other
            // displays unable to take a proper drag on mixed multi-monitor setups.
            _frame = new DispatcherFrame();
            foreach (var window in _windows)
                window.Show();

            var active = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            var focus = _windows.FirstOrDefault(window => window.TargetScreen.DeviceName == active.DeviceName)
                        ?? _windows.FirstOrDefault(window => window.TargetScreen.Primary)
                        ?? _windows[0];
            focus.Activate();
            focus.Focus();

            Dispatcher.PushFrame(_frame);
            pick = _pick;
            return _pick is not null;
        }
        finally
        {
            foreach (var window in _windows)
            {
                if (window.IsLoaded)
                    window.Close();
            }

            _frame = null;
        }
    }

    public void BeginDrag(DrawingPoint pixel)
    {
        if (_done)
            return;
        _anchor = pixel;
        _dragging = true;
        Publish(null);
    }

    public void DragMove(DrawingPoint pixel, bool square)
    {
        if (!_dragging || _done)
            return;
        Publish(MakeRect(Constrain(pixel, square)));
    }

    public void DragEnd(DrawingPoint pixel, bool square)
    {
        if (!_dragging || _done)
            return;
        _dragging = false;
        var rect = MakeRect(Constrain(pixel, square));
        if (rect.Width < 3 || rect.Height < 3)
        {
            Publish(null);
            return;
        }

        Finish(rect);
    }

    public void Cancel()
    {
        if (_done)
            return;
        _done = true;
        _dragging = false;
        _pick = null;
        StopFrame();
    }

    private void Finish(DrawingRectangle rect)
    {
        _done = true;
        var hits = Screen.AllScreens
            .Select(screen => (Screen: screen, Area: DrawingRectangle.Intersect(screen.Bounds, rect)))
            .Where(hit => hit.Area.Width > 0 && hit.Area.Height > 0)
            .OrderByDescending(hit => hit.Area.Width * (long)hit.Area.Height)
            .ToArray();
        if (hits.Length == 0)
        {
            _done = false;
            Cancel();
            return;
        }

        var host = hits[0].Screen;
        var crop = WithMonitorDpi(Crop(_desktop.Image, _desktop.Bounds, rect), host.Bounds);
        var contained = hits.Length == 1 && host.Bounds.Contains(rect);
        if (contained && host.Bounds.Width > 0 && host.Bounds.Height > 0)
        {
            _pick = new RegionPick
            {
                Image = crop,
                Background = WithMonitorDpi(Crop(_desktop.Image, _desktop.Bounds, host.Bounds), host.Bounds),
                Fractions = new NormalizedRect(
                    (rect.Left - host.Bounds.Left) / (double)host.Bounds.Width,
                    (rect.Top - host.Bounds.Top) / (double)host.Bounds.Height,
                    rect.Width / (double)host.Bounds.Width,
                    rect.Height / (double)host.Bounds.Height),
                Cover = host.Bounds,
                InPlace = true
            };
        }
        else
        {
            _pick = new RegionPick
            {
                Image = crop,
                Cover = host.Bounds,
                InPlace = false
            };
        }

        StopFrame();
    }

    private void StopFrame()
    {
        if (_frame is { } frame)
            frame.Continue = false;
    }

    private void Publish(DrawingRectangle? selection)
    {
        foreach (var window in _windows)
            window.ShowSelection(selection);
    }

    private DrawingPoint Constrain(DrawingPoint pixel, bool square)
    {
        if (!square)
            return pixel;

        var dx = pixel.X - _anchor.X;
        var dy = pixel.Y - _anchor.Y;
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new DrawingPoint(
            _anchor.X + Math.Sign(dx == 0 ? 1 : dx) * side,
            _anchor.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
    }

    private DrawingRectangle MakeRect(DrawingPoint pixel)
    {
        var left = Math.Min(_anchor.X, pixel.X);
        var top = Math.Min(_anchor.Y, pixel.Y);
        var right = Math.Max(_anchor.X, pixel.X);
        var bottom = Math.Max(_anchor.Y, pixel.Y);
        left = Math.Clamp(left, _desktop.Bounds.Left, Math.Max(_desktop.Bounds.Left, _desktop.Bounds.Right - 1));
        top = Math.Clamp(top, _desktop.Bounds.Top, Math.Max(_desktop.Bounds.Top, _desktop.Bounds.Bottom - 1));
        right = Math.Clamp(right, left + 1, _desktop.Bounds.Right);
        bottom = Math.Clamp(bottom, top + 1, _desktop.Bounds.Bottom);
        return DrawingRectangle.FromLTRB(left, top, right, bottom);
    }

    internal static BitmapSource Crop(BitmapSource image, DrawingRectangle imageBounds, DrawingRectangle virtualRect)
    {
        var x = virtualRect.Left - imageBounds.Left;
        var y = virtualRect.Top - imageBounds.Top;
        var left = Math.Clamp(x, 0, Math.Max(0, image.PixelWidth - 1));
        var top = Math.Clamp(y, 0, Math.Max(0, image.PixelHeight - 1));
        var width = Math.Clamp(virtualRect.Width - Math.Max(0, left - x), 1, image.PixelWidth - left);
        var height = Math.Clamp(virtualRect.Height - Math.Max(0, top - y), 1, image.PixelHeight - top);
        var cropped = new CroppedBitmap(image, new Int32Rect(left, top, width, height));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>
    /// Screen captures are physical pixels. Stamp the monitor DPI so WPF sizes the
    /// image in DIPs for that display instead of treating every pixel as 96 DPI.
    /// </summary>
    internal static BitmapSource WithMonitorDpi(BitmapSource source, DrawingRectangle monitorPixels)
    {
        DisplayLayout.GetMonitorDpi(monitorPixels, out var dpiX, out var dpiY);
        if (Math.Abs(source.DpiX - dpiX) < 0.5 && Math.Abs(source.DpiY - dpiY) < 0.5)
            return source;

        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        var stamped = BitmapSource.Create(
            bgra.PixelWidth,
            bgra.PixelHeight,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        stamped.Freeze();
        return stamped;
    }
}
