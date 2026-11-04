using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipShot;

public partial class ScreenshotEditorWindow : Window
{
    private readonly BitmapSource _image;

    public bool WasCopied { get; private set; }
    public bool WasSaved { get; private set; }

    public ScreenshotEditorWindow(BitmapSource image)
    {
        InitializeComponent();
        _image = image;
        PreviewImage.Source = image;
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Red,
            Width = 3,
            Height = 3,
            FitToCurve = true,
            IgnorePressure = true
        };
        AnnotationCanvas.Width = image.PixelWidth * 96d / image.DpiX;
        AnnotationCanvas.Height = image.PixelHeight * 96d / image.DpiY;
        PreviewImage.Width = AnnotationCanvas.Width;
        PreviewImage.Height = AnnotationCanvas.Height;
    }

    private void Pen_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.EditingMode = InkCanvasEditingMode.Ink;
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Red, Width = 3, Height = 3, FitToCurve = true, IgnorePressure = true
        };
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.EditingMode = InkCanvasEditingMode.Ink;
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromArgb(110, 255, 220, 0), Width = 18, Height = 18,
            FitToCurve = true, IgnorePressure = true, IsHighlighter = true
        };
    }

    private void Erase_Click(object sender, RoutedEventArgs e) =>
        AnnotationCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas.Strokes.Count > 0)
            AnnotationCanvas.Strokes.RemoveAt(AnnotationCanvas.Strokes.Count - 1);
    }

    private BitmapSource RenderAnnotatedImage()
    {
        var width = Math.Max(1, (int)Math.Ceiling(AnnotationCanvas.Width));
        var height = Math.Max(1, (int)Math.Ceiling(AnnotationCanvas.Height));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(_image, new Rect(0, 0, width, height));
            AnnotationCanvas.Measure(new Size(width, height));
            AnnotationCanvas.Arrange(new Rect(0, 0, width, height));
            context.DrawVisual(AnnotationCanvas);
        }
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetImage(RenderAnnotatedImage());
            System.Windows.Clipboard.Flush();
            if (!System.Windows.Clipboard.ContainsImage())
                throw new InvalidOperationException("Windows did not confirm the image reached the clipboard.");
            WasCopied = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Clipboard error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"ClipShot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderAnnotatedImage()));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            WasSaved = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}