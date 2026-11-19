using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ClipShot.Services;

public sealed class DesktopCapture
{
    public required BitmapSource Image { get; init; }
    public required DrawingRectangle Bounds { get; init; }
}

public sealed class CaptureService
{
    public DesktopCapture CaptureDesktop()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Windows could not find a display.");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0)
                    continue;

                try
                {
                    graphics.CopyFromScreen(
                        screen.Bounds.Left,
                        screen.Bounds.Top,
                        screen.Bounds.Left - bounds.Left,
                        screen.Bounds.Top - bounds.Top,
                        screen.Bounds.Size,
                        CopyPixelOperation.SourceCopy);
                }
                catch (Exception)
                {
                    // A display can refuse a copy while it is disconnecting. Keep the rest.
                }
            }
        }

        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return new DesktopCapture { Image = source, Bounds = bounds };
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }
}
