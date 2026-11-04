using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Input;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.Windows.Shapes.Path;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipShot;

public partial class ScreenshotEditorWindow : Window
{
    private enum EditorTool { Pen, Highlight, Erase, Select, Arrow, Line, Rectangle, Ellipse, Text, Number, Blur, Pixelate }
    private EditorTool _tool = EditorTool.Pen;
    private Point _start;
    private Shape? _previewShape;
    private BitmapSource? _beforeAction;
    private readonly Stack<BitmapSource> _undo = new();
    private readonly Stack<BitmapSource> _redo = new();
    private int _stepNumber = 1;
    private bool _drawing;
    private bool _eraseChanged;

    public bool WasCopied { get; private set; }
    public bool WasSaved { get; private set; }

    public ScreenshotEditorWindow(BitmapSource image)
    {
        InitializeComponent();
        PreviewImage.Source = image;
        AnnotationCanvas.Width = image.PixelWidth * 96d / image.DpiX;
        AnnotationCanvas.Height = image.PixelHeight * 96d / image.DpiY;
        PreviewImage.Width = AnnotationCanvas.Width;
        PreviewImage.Height = AnnotationCanvas.Height;
        PreviewSurface.Width = AnnotationCanvas.Width;
        PreviewSurface.Height = AnnotationCanvas.Height;
        SetTool(EditorTool.Pen);
    }

    private void SetTool(EditorTool tool)
    {
        _tool = tool;
        AnnotationCanvas.EditingMode = tool switch
        {
            EditorTool.Pen or EditorTool.Highlight => InkCanvasEditingMode.Ink,
            EditorTool.Erase => InkCanvasEditingMode.None,
            EditorTool.Select => InkCanvasEditingMode.Select,
            _ => InkCanvasEditingMode.None
        };
        AnnotationCanvas.Cursor = tool is EditorTool.Blur or EditorTool.Pixelate ? Cursors.Cross : Cursors.Pen;
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = tool == EditorTool.Highlight ? Color.FromArgb(105, 255, 220, 0) : Colors.Red,
            Width = tool == EditorTool.Highlight ? 20 : 3,
            Height = tool == EditorTool.Highlight ? 20 : 3,
            FitToCurve = true,
            IgnorePressure = true,
            IsHighlighter = tool == EditorTool.Highlight
        };
    }

    private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pen);
    private void Highlight_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Highlight);
    private void Erase_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Erase);
    private void Select_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Select);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        { Redo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z)
        { Undo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Y)
        { Redo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Delete)
        { DeleteSelection(); e.Handled = true; }
    }

    private void DeleteSelection_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void DeleteSelection()
    {
        if (AnnotationCanvas.GetSelectedStrokes().Count == 0 && AnnotationCanvas.GetSelectedElements().Count == 0) return;
        SaveUndoPoint();
        AnnotationCanvas.GetSelectedStrokes().Clear();
        foreach (var element in AnnotationCanvas.GetSelectedElements().Cast<UIElement>().ToArray())
            AnnotationCanvas.Children.Remove(element);
    }
    private void Arrow_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Arrow);
    private void Line_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Line);
    private void Rectangle_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Rectangle);
    private void Ellipse_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Ellipse);
    private void Text_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Text);
    private void Number_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Number);
    private void Blur_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Blur);
    private void Pixelate_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pixelate);

    private BitmapSource RenderAnnotatedImage()
    {
        var width = Math.Max(1, (int)Math.Ceiling(PreviewSurface.Width));
        var height = Math.Max(1, (int)Math.Ceiling(PreviewSurface.Height));
        PreviewSurface.Measure(new Size(width, height));
        PreviewSurface.Arrange(new Rect(0, 0, width, height));
        PreviewSurface.UpdateLayout();
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(PreviewSurface);
        rendered.Freeze();
        return rendered;
    }

    private void SaveUndoPoint()
    {
        _undo.Push(RenderAnnotatedImage());
        _redo.Clear();
    }

    private void ApplySnapshot(BitmapSource snapshot)
    {
        PreviewImage.Source = snapshot;
        PreviewImage.Width = snapshot.PixelWidth;
        PreviewImage.Height = snapshot.PixelHeight;
        AnnotationCanvas.Width = snapshot.PixelWidth;
        AnnotationCanvas.Height = snapshot.PixelHeight;
        PreviewSurface.Width = snapshot.PixelWidth;
        PreviewSurface.Height = snapshot.PixelHeight;
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        _redo.Push(RenderAnnotatedImage());
        ApplySnapshot(_undo.Pop());
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        _undo.Push(RenderAnnotatedImage());
        ApplySnapshot(_redo.Pop());
    }

    private void AnnotationCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        // Stroke undo is captured on pointer-down so the complete previous state is retained.
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool is EditorTool.Pen or EditorTool.Highlight)
        {
            _beforeAction = RenderAnnotatedImage();
            _drawing = true;
            return;
        }
        if (_tool == EditorTool.Erase)
        {
            _beforeAction = RenderAnnotatedImage();
            _drawing = true;
            _eraseChanged = false;
            EraseAt(e.GetPosition(AnnotationCanvas));
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        _start = e.GetPosition(AnnotationCanvas);
        _beforeAction = RenderAnnotatedImage();
        _drawing = true;

        if (_tool == EditorTool.Text)
        {
            var text = PromptForText("Add text", "Enter annotation text:");
            if (!string.IsNullOrWhiteSpace(text))
            {
                SaveUndoPointFrom(_beforeAction);
                AnnotationCanvas.Children.Add(new TextBlock
                {
                    Text = text, FontSize = 24, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Red, Background = Brushes.Transparent
                }.WithPosition(_start));
            }
            _drawing = false;
            return;
        }

        if (_tool == EditorTool.Number)
        {
            SaveUndoPointFrom(_beforeAction);
            var marker = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
                Background = Brushes.Red, BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = (_stepNumber++).ToString(), Foreground = Brushes.White,
                    FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center }
            };
            AnnotationCanvas.Children.Add(marker);
            Canvas.SetLeft(marker, _start.X - 17);
            Canvas.SetTop(marker, _start.Y - 17);
            _drawing = false;
            return;
        }

        if (_tool is EditorTool.Arrow or EditorTool.Line or EditorTool.Rectangle or EditorTool.Ellipse)
        {
            _previewShape = CreateShape(_tool);
            AnnotationCanvas.Children.Add(_previewShape);
            UpdateShape(e.GetPosition(AnnotationCanvas));
        }
        else if (_tool is EditorTool.Blur or EditorTool.Pixelate)
        {
            _previewShape = new Rectangle { Stroke = Brushes.OrangeRed, StrokeDashArray = new DoubleCollection { 3, 2 }, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 255, 120, 0)) };
            AnnotationCanvas.Children.Add(_previewShape);
            UpdateShape(e.GetPosition(AnnotationCanvas));
        }
        AnnotationCanvas.CaptureMouse();
    }

    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var position = e.GetPosition(AnnotationCanvas);
        if (_tool == EditorTool.Erase)
        {
            EraseAt(position);
            e.Handled = true;
        }
        else if (_previewShape is not null)
            UpdateShape(position);
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        if (_tool is EditorTool.Pen or EditorTool.Highlight)
        {
            _drawing = false;
            if (_beforeAction is not null) SaveUndoPointFrom(_beforeAction);
            return;
        }
        if (_tool == EditorTool.Erase)
        {
            AnnotationCanvas.ReleaseMouseCapture();
            _drawing = false;
            if (_beforeAction is not null && _eraseChanged) SaveUndoPointFrom(_beforeAction);
            _eraseChanged = false;
            e.Handled = true;
            return;
        }
        if (_previewShape is null) return;
        AnnotationCanvas.ReleaseMouseCapture();
        var end = e.GetPosition(AnnotationCanvas);
        UpdateShape(end);
        AnnotationCanvas.Children.Remove(_previewShape);
        _previewShape = null;
        _drawing = false;

        var rect = MakeRect(_start, end);
        if (rect.Width < 3 || rect.Height < 3) return;

        if (_tool is EditorTool.Blur or EditorTool.Pixelate)
        {
            if (_beforeAction is not null) SaveUndoPointFrom(_beforeAction);
            ApplyMosaic(rect, _tool == EditorTool.Blur ? 7 : 16);
            return;
        }

        if (_beforeAction is not null) SaveUndoPointFrom(_beforeAction);
        var shape = CreateShape(_tool);
        if (shape is Line line)
        {
            line.X1 = _start.X; line.Y1 = _start.Y; line.X2 = end.X; line.Y2 = end.Y;
        }
        else
        {
            if (shape is Path arrow)
            {
                Canvas.SetLeft(shape, 0); Canvas.SetTop(shape, 0);
                arrow.Data = BuildArrowGeometry(_start, end);
            }
            else
            {
                Canvas.SetLeft(shape, rect.Left); Canvas.SetTop(shape, rect.Top);
                shape.Width = rect.Width; shape.Height = rect.Height;
            }
        }
        AnnotationCanvas.Children.Add(shape);
    }

    private static Shape CreateShape(EditorTool tool) => tool switch
    {
        EditorTool.Line => new Line { Stroke = Brushes.Red, StrokeThickness = 3 },
        EditorTool.Arrow => new Path { Stroke = Brushes.Red, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Red },
        EditorTool.Ellipse => new Ellipse { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent },
        _ => new Rectangle { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent }
    };

    private void UpdateShape(Point end)
    {
        if (_previewShape is null) return;
        var rect = MakeRect(_start, end);
        if (_previewShape is Line line)
        {
            line.X1 = _start.X; line.Y1 = _start.Y; line.X2 = end.X; line.Y2 = end.Y;
        }
        else if (_previewShape is Path arrow)
        {
            Canvas.SetLeft(arrow, 0); Canvas.SetTop(arrow, 0);
            arrow.Data = BuildArrowGeometry(_start, end);
        }
        else
        {
            Canvas.SetLeft(_previewShape, rect.Left); Canvas.SetTop(_previewShape, rect.Top);
            _previewShape.Width = Math.Max(1, rect.Width);
            _previewShape.Height = Math.Max(1, rect.Height);
        }
    }

    private static Geometry BuildArrowGeometry(Point start, Point end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return Geometry.Empty;
        double ux = dx / length, uy = dy / length;
        double headLength = Math.Min(22, Math.Max(12, length * 0.28));
        double halfWidth = headLength * 0.55;
        var basePoint = new Point(end.X - ux * headLength, end.Y - uy * headLength);
        double px = -uy * halfWidth, py = ux * halfWidth;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.LineTo(end, true, false);
            ctx.BeginFigure(new Point(basePoint.X + px, basePoint.Y + py), true, true);
            ctx.LineTo(end, true, false);
            ctx.LineTo(new Point(basePoint.X - px, basePoint.Y - py), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void EraseAt(Point point)
    {
        const double radius = 14;
        var hitElements = AnnotationCanvas.Children.Cast<UIElement>()
            .Where(element => element is Shape || element is TextBlock || element is Border)
            .Where(element =>
            {
                var bounds = element is Shape shape
                    ? shape.RenderedGeometry.Bounds
                    : new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height);
                double left = Canvas.GetLeft(element), top = Canvas.GetTop(element);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                var hitBounds = new Rect(left + bounds.Left, top + bounds.Top,
                    Math.Max(bounds.Width, 1), Math.Max(bounds.Height, 1));
                hitBounds.Inflate(radius, radius);
                return hitBounds.Contains(point);
            }).ToArray();
        foreach (var element in hitElements)
        {
            AnnotationCanvas.Children.Remove(element);
            _eraseChanged = true;
        }
        var hitStrokes = AnnotationCanvas.Strokes.Where(stroke =>
            stroke.HitTest(point, (int)(radius * 2))).ToArray();
        foreach (var stroke in hitStrokes)
        {
            AnnotationCanvas.Strokes.Remove(stroke);
            _eraseChanged = true;
        }
    }

    private static Rect MakeRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void SaveUndoPointFrom(BitmapSource snapshot)
    {
        _undo.Push(snapshot);
        _redo.Clear();
    }

    private void ApplyMosaic(Rect selection, int block)
    {
        var source = PreviewImage.Source as BitmapSource;
        if (source is null) return;
        var x = Math.Clamp((int)selection.Left, 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)selection.Top, 0, source.PixelHeight - 1);
        var w = Math.Clamp((int)selection.Width, 1, source.PixelWidth - x);
        var h = Math.Clamp((int)selection.Height, 1, source.PixelHeight - y);
        var crop = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
        var converted = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);
        var smallW = Math.Max(1, (w + block - 1) / block);
        var smallH = Math.Max(1, (h + block - 1) / block);
        var small = new byte[smallW * smallH * 4];
        for (int by = 0; by < smallH; by++)
        for (int bx = 0; bx < smallW; bx++)
        {
            long bsum = 0, gsum = 0, rsum = 0, asum = 0, count = 0;
            for (int py = by * block; py < Math.Min(h, (by + 1) * block); py++)
            for (int px = bx * block; px < Math.Min(w, (bx + 1) * block); px++)
            {
                int i = py * stride + px * 4;
                bsum += pixels[i]; gsum += pixels[i + 1]; rsum += pixels[i + 2]; asum += pixels[i + 3]; count++;
            }
            int o = (by * smallW + bx) * 4;
            small[o] = (byte)(bsum / count); small[o + 1] = (byte)(gsum / count);
            small[o + 2] = (byte)(rsum / count); small[o + 3] = (byte)(asum / count);
        }
        var effect = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var expanded = new byte[h * stride];
        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            int src = ((py / block) * smallW + (px / block)) * 4;
            Buffer.BlockCopy(small, src, expanded, py * stride + px * 4, 4);
        }
        effect.WritePixels(new Int32Rect(0, 0, w, h), expanded, stride, 0);
        effect.Freeze();
        var full = new DrawingVisual();
        using (var dc = full.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            dc.DrawImage(effect, new Rect(x, y, w, h));
        }
        var rendered = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(full); rendered.Freeze();
        ApplySnapshot(rendered);
    }

    private static string? PromptForText(string title, string prompt)
    {
        var dialog = new Window { Title = title, Width = 360, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { FontSize = 14 };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Add", IsDefault = true, Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 70 };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        ok.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(RenderAnnotatedImage());
            Clipboard.Flush();
            WasCopied = true;
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard error", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Save screenshot", Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png", AddExtension = true, FileName = $"ClipShot-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

internal static class FrameworkElementExtensions
{
    public static T WithPosition<T>(this T element, Point point) where T : FrameworkElement
    {
        Canvas.SetLeft(element, point.X);
        Canvas.SetTop(element, point.Y);
        return element;
    }
}