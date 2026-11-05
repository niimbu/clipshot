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
    private Border? _inlineTextHost;
    private TextBox? _inlineTextBox;
    private BitmapSource? _inlineTextUndo;

    public bool WasCopied { get; private set; }
    public bool WasSaved { get; private set; }

    public ScreenshotEditorWindow(BitmapSource image)
    {
        InitializeComponent();
        PreviewImage.Source = image;
        var width = image.PixelWidth * 96d / image.DpiX;
        var height = image.PixelHeight * 96d / image.DpiY;
        AnnotationCanvas.Width = width;
        AnnotationCanvas.Height = height;
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        PreviewSurface.Width = width;
        PreviewSurface.Height = height;
        SetTool(EditorTool.Pen);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        AnnotationCanvas.Focus();
    }

    private void SetTool(EditorTool tool)
    {
        CommitInlineText(save: true);
        _tool = tool;
        AnnotationCanvas.EditingMode = tool switch
        {
            EditorTool.Pen or EditorTool.Highlight => InkCanvasEditingMode.Ink,
            EditorTool.Erase => InkCanvasEditingMode.EraseByPoint,
            EditorTool.Select => InkCanvasEditingMode.Select,
            _ => InkCanvasEditingMode.None
        };
        if (tool == EditorTool.Erase)
            AnnotationCanvas.EraserShape = new EllipseStylusShape(22, 22);

        AnnotationCanvas.Cursor = tool switch
        {
            EditorTool.Blur or EditorTool.Pixelate or EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Arrow or EditorTool.Line => Cursors.Cross,
            EditorTool.Erase => Cursors.Cross,
            EditorTool.Select => Cursors.Arrow,
            EditorTool.Text or EditorTool.Number => Cursors.IBeam,
            _ => Cursors.Pen
        };
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = tool == EditorTool.Highlight ? Color.FromArgb(105, 255, 220, 0) : Colors.Red,
            Width = tool == EditorTool.Highlight ? 20 : 3,
            Height = tool == EditorTool.Highlight ? 20 : 3,
            FitToCurve = true,
            IgnorePressure = true,
            IsHighlighter = tool == EditorTool.Highlight
        };
        UpdateActiveToolButton();
    }

    private void UpdateActiveToolButton()
    {
        foreach (var button in new[]
                 {
                     PenButton, HighlightButton, ArrowButton, LineButton, RectangleButton, EllipseButton,
                     TextButton, NumberButton, BlurButton, PixelateButton, EraseButton, SelectButton
                 })
            button.Tag = null;

        Button active = _tool switch
        {
            EditorTool.Pen => PenButton,
            EditorTool.Highlight => HighlightButton,
            EditorTool.Arrow => ArrowButton,
            EditorTool.Line => LineButton,
            EditorTool.Rectangle => RectangleButton,
            EditorTool.Ellipse => EllipseButton,
            EditorTool.Text => TextButton,
            EditorTool.Number => NumberButton,
            EditorTool.Blur => BlurButton,
            EditorTool.Pixelate => PixelateButton,
            EditorTool.Erase => EraseButton,
            _ => SelectButton
        };
        active.Tag = "Active";
    }

    private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pen);
    private void Highlight_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Highlight);
    private void Erase_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Erase);
    private void Select_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Select);
    private void Arrow_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Arrow);
    private void Line_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Line);
    private void Rectangle_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Rectangle);
    private void Ellipse_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Ellipse);
    private void Text_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Text);
    private void Number_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Number);
    private void Blur_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Blur);
    private void Pixelate_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pixelate);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_inlineTextHost is not null && e.Key is Key.Enter or Key.Escape)
            return;

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            Redo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            CopyAndClose();
            e.Handled = true;
        }
    }

    private void DeleteSelection_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void DeleteSelection()
    {
        CommitInlineText(save: false);

        var selectedStrokes = AnnotationCanvas.GetSelectedStrokes();
        var selectedElements = AnnotationCanvas.GetSelectedElements().Cast<UIElement>().ToArray();
        if (selectedStrokes.Count > 0 || selectedElements.Length > 0)
        {
            SaveUndoPoint();
            selectedStrokes.Clear();
            foreach (var element in selectedElements)
                AnnotationCanvas.Children.Remove(element);
            return;
        }

        // Nothing selected: remove the most recent annotation so Delete always does something useful.
        if (AnnotationCanvas.Children.Count > 0)
        {
            SaveUndoPoint();
            AnnotationCanvas.Children.RemoveAt(AnnotationCanvas.Children.Count - 1);
            return;
        }

        if (AnnotationCanvas.Strokes.Count > 0)
        {
            SaveUndoPoint();
            AnnotationCanvas.Strokes.RemoveAt(AnnotationCanvas.Strokes.Count - 1);
        }
    }

    private BitmapSource RenderAnnotatedImage()
    {
        var source = PreviewImage.Source as BitmapSource;
        var pixelWidth = source?.PixelWidth ?? Math.Max(1, (int)Math.Ceiling(PreviewSurface.Width));
        var pixelHeight = source?.PixelHeight ?? Math.Max(1, (int)Math.Ceiling(PreviewSurface.Height));
        var dpiX = source?.DpiX > 0 ? source.DpiX : 96d;
        var dpiY = source?.DpiY > 0 ? source.DpiY : 96d;
        var dipWidth = pixelWidth * 96d / dpiX;
        var dipHeight = pixelHeight * 96d / dpiY;

        PreviewSurface.Measure(new Size(dipWidth, dipHeight));
        PreviewSurface.Arrange(new Rect(0, 0, dipWidth, dipHeight));
        PreviewSurface.UpdateLayout();

        var rendered = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
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
        var displayWidth = snapshot.PixelWidth * 96d / snapshot.DpiX;
        var displayHeight = snapshot.PixelHeight * 96d / snapshot.DpiY;
        PreviewImage.Width = displayWidth;
        PreviewImage.Height = displayHeight;
        AnnotationCanvas.Width = displayWidth;
        AnnotationCanvas.Height = displayHeight;
        PreviewSurface.Width = displayWidth;
        PreviewSurface.Height = displayHeight;
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(RenderAnnotatedImage());
        ApplySnapshot(_undo.Pop());
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(RenderAnnotatedImage());
        ApplySnapshot(_redo.Pop());
    }

    private void AnnotationCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        // Stroke undo is captured on pointer-down so the complete previous state is retained.
    }

    private void AnnotationCanvas_StrokeErasing(object sender, InkCanvasStrokeErasingEventArgs e)
    {
        if (!_drawing)
        {
            _beforeAction = RenderAnnotatedImage();
            _drawing = true;
            _eraseChanged = false;
        }
        _eraseChanged = true;
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(AnnotationCanvas);

        if (_inlineTextHost is not null && !_inlineTextHost.IsMouseOver)
            CommitInlineText(save: true);

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
            EraseElementsAt(position);
            // Do not capture the mouse — EraseByPoint needs the normal ink routing for strokes.
            e.Handled = false;
            return;
        }

        if (_tool == EditorTool.Select)
            return;

        _start = position;
        _beforeAction = RenderAnnotatedImage();
        _drawing = true;

        if (_tool == EditorTool.Text)
        {
            BeginInlineText(position);
            _drawing = false;
            e.Handled = true;
            return;
        }

        if (_tool == EditorTool.Number)
        {
            SaveUndoPointFrom(_beforeAction);
            var marker = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = Brushes.Red,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = (_stepNumber++).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            PlaceElement(marker, _start.X - 17, _start.Y - 17);
            AnnotationCanvas.Children.Add(marker);
            _drawing = false;
            e.Handled = true;
            return;
        }

        if (_tool is EditorTool.Arrow or EditorTool.Line or EditorTool.Rectangle or EditorTool.Ellipse)
        {
            _previewShape = CreateShape(_tool);
            AnnotationCanvas.Children.Add(_previewShape);
            UpdateShape(position);
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_tool is EditorTool.Blur or EditorTool.Pixelate)
        {
            _previewShape = new Rectangle
            {
                Stroke = Brushes.OrangeRed,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(35, 255, 120, 0))
            };
            AnnotationCanvas.Children.Add(_previewShape);
            UpdateShape(position);
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing)
            return;

        var position = e.GetPosition(AnnotationCanvas);
        if (_tool == EditorTool.Erase)
        {
            EraseElementsAt(position);
            e.Handled = false;
        }
        else if (_previewShape is not null)
        {
            UpdateShape(position);
            e.Handled = true;
        }
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
            return;

        if (_tool is EditorTool.Pen or EditorTool.Highlight)
        {
            _drawing = false;
            if (_beforeAction is not null)
                SaveUndoPointFrom(_beforeAction);
            return;
        }

        if (_tool == EditorTool.Erase)
        {
            _drawing = false;
            if (_beforeAction is not null && _eraseChanged)
                SaveUndoPointFrom(_beforeAction);
            _eraseChanged = false;
            e.Handled = false;
            return;
        }

        if (_previewShape is null)
        {
            _drawing = false;
            return;
        }

        AnnotationCanvas.ReleaseMouseCapture();
        var end = e.GetPosition(AnnotationCanvas);
        UpdateShape(end);
        AnnotationCanvas.Children.Remove(_previewShape);
        _previewShape = null;
        _drawing = false;
        e.Handled = true;

        var rect = MakeRect(_start, end);
        if (rect.Width < 3 || rect.Height < 3)
            return;

        if (_tool is EditorTool.Blur or EditorTool.Pixelate)
        {
            if (_beforeAction is not null)
                SaveUndoPointFrom(_beforeAction);
            ApplyMosaic(rect, _tool == EditorTool.Blur ? 7 : 16);
            return;
        }

        if (_beforeAction is not null)
            SaveUndoPointFrom(_beforeAction);

        var shape = CreateShape(_tool);
        if (shape is Line line)
        {
            line.X1 = _start.X;
            line.Y1 = _start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            PlaceElement(shape, 0, 0);
        }
        else if (shape is Path arrow)
        {
            PlaceElement(arrow, 0, 0);
            arrow.Data = BuildArrowGeometry(_start, end);
        }
        else
        {
            PlaceElement(shape, rect.Left, rect.Top);
            shape.Width = rect.Width;
            shape.Height = rect.Height;
        }

        AnnotationCanvas.Children.Add(shape);
    }

    private static Shape CreateShape(EditorTool tool) => tool switch
    {
        EditorTool.Line => new Line { Stroke = Brushes.Red, StrokeThickness = 3 },
        EditorTool.Arrow => new Path
        {
            Stroke = Brushes.Red,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Red
        },
        EditorTool.Ellipse => new Ellipse { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent },
        _ => new Rectangle { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent }
    };

    private void UpdateShape(Point end)
    {
        if (_previewShape is null)
            return;

        var rect = MakeRect(_start, end);
        if (_previewShape is Line line)
        {
            PlaceElement(line, 0, 0);
            line.X1 = _start.X;
            line.Y1 = _start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
        }
        else if (_previewShape is Path arrow)
        {
            PlaceElement(arrow, 0, 0);
            arrow.Data = BuildArrowGeometry(_start, end);
        }
        else
        {
            PlaceElement(_previewShape, rect.Left, rect.Top);
            _previewShape.Width = Math.Max(1, rect.Width);
            _previewShape.Height = Math.Max(1, rect.Height);
        }
    }

    private static void PlaceElement(UIElement element, double left, double top)
    {
        // InkCanvas ignores Canvas.Left/Top. Use its own attached properties so shapes
        // follow the pointer instead of sticking to the top-left corner.
        InkCanvas.SetLeft(element, left);
        InkCanvas.SetTop(element, top);
    }

    private static Geometry BuildArrowGeometry(Point start, Point end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return Geometry.Empty;

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

    private void EraseElementsAt(Point point)
    {
        const double radius = 18;
        var hitElements = AnnotationCanvas.Children.Cast<UIElement>()
            .Where(element => element is not Border host || host != _inlineTextHost)
            .Where(element => element is Shape or TextBlock or Border)
            .Where(element => HitTestAnnotation(element, point, radius))
            .ToArray();

        foreach (var element in hitElements)
        {
            AnnotationCanvas.Children.Remove(element);
            _eraseChanged = true;
        }
    }

    private static bool HitTestAnnotation(UIElement element, Point point, double radius)
    {
        var left = InkCanvas.GetLeft(element);
        var top = InkCanvas.GetTop(element);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        Rect bounds;
        if (element is Line line)
        {
            bounds = new Rect(
                Math.Min(line.X1, line.X2),
                Math.Min(line.Y1, line.Y2),
                Math.Max(1, Math.Abs(line.X2 - line.X1)),
                Math.Max(1, Math.Abs(line.Y2 - line.Y1)));
        }
        else if (element is Shape shape)
        {
            var geometryBounds = shape.RenderedGeometry.Bounds;
            if (geometryBounds.IsEmpty)
                bounds = new Rect(0, 0, Math.Max(1, shape.Width), Math.Max(1, shape.Height));
            else
                bounds = geometryBounds;
        }
        else
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = element.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
                size = element.RenderSize;
            bounds = new Rect(0, 0, Math.Max(size.Width, 8), Math.Max(size.Height, 8));
        }

        var hitBounds = new Rect(left + bounds.Left, top + bounds.Top,
            Math.Max(bounds.Width, 1), Math.Max(bounds.Height, 1));
        hitBounds.Inflate(radius, radius);
        return hitBounds.Contains(point);
    }

    private void BeginInlineText(Point position)
    {
        CommitInlineText(save: true);
        _inlineTextUndo = _beforeAction;

        _inlineTextBox = new TextBox
        {
            MinWidth = 140,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Red,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brushes.Red,
            Padding = new Thickness(2, 0, 2, 0)
        };
        _inlineTextBox.KeyDown += InlineTextBox_KeyDown;
        _inlineTextBox.LostKeyboardFocus += (_, _) => CommitInlineText(save: true);

        _inlineTextHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderBrush = Brushes.Red,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            Child = _inlineTextBox
        };

        PlaceElement(_inlineTextHost, position.X, position.Y);
        AnnotationCanvas.Children.Add(_inlineTextHost);
        Dispatcher.BeginInvoke(() =>
        {
            _inlineTextBox?.Focus();
            Keyboard.Focus(_inlineTextBox);
        });
    }

    private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInlineText(save: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitInlineText(save: false);
            e.Handled = true;
        }
    }

    private void CommitInlineText(bool save)
    {
        if (_inlineTextHost is null)
            return;

        var text = _inlineTextBox?.Text?.Trim() ?? string.Empty;
        var left = InkCanvas.GetLeft(_inlineTextHost);
        var top = InkCanvas.GetTop(_inlineTextHost);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        if (_inlineTextBox is not null)
            _inlineTextBox.KeyDown -= InlineTextBox_KeyDown;

        AnnotationCanvas.Children.Remove(_inlineTextHost);
        _inlineTextHost = null;
        _inlineTextBox = null;

        if (!save || string.IsNullOrWhiteSpace(text))
        {
            _inlineTextUndo = null;
            return;
        }

        if (_inlineTextUndo is not null)
            SaveUndoPointFrom(_inlineTextUndo);
        else
            SaveUndoPoint();

        var block = new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Red,
            Background = Brushes.Transparent
        };
        PlaceElement(block, left, top);
        AnnotationCanvas.Children.Add(block);
        _inlineTextUndo = null;
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
        if (source is null)
            return;

        var overlayWidth = Math.Max(1d, AnnotationCanvas.Width);
        var overlayHeight = Math.Max(1d, AnnotationCanvas.Height);
        var scaleX = source.PixelWidth / overlayWidth;
        var scaleY = source.PixelHeight / overlayHeight;
        var left = Math.Clamp(Math.Min(selection.Left, selection.Right), 0, overlayWidth);
        var top = Math.Clamp(Math.Min(selection.Top, selection.Bottom), 0, overlayHeight);
        var right = Math.Clamp(Math.Max(selection.Left, selection.Right), 0, overlayWidth);
        var bottom = Math.Clamp(Math.Max(selection.Top, selection.Bottom), 0, overlayHeight);
        var x = Math.Clamp((int)Math.Floor(left * scaleX), 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(top * scaleY), 0, source.PixelHeight - 1);
        var endX = Math.Clamp((int)Math.Ceiling(right * scaleX), x + 1, source.PixelWidth);
        var endY = Math.Clamp((int)Math.Ceiling(bottom * scaleY), y + 1, source.PixelHeight);
        var w = endX - x;
        var h = endY - y;
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
                bsum += pixels[i];
                gsum += pixels[i + 1];
                rsum += pixels[i + 2];
                asum += pixels[i + 3];
                count++;
            }

            int o = (by * smallW + bx) * 4;
            small[o] = (byte)(bsum / count);
            small[o + 1] = (byte)(gsum / count);
            small[o + 2] = (byte)(rsum / count);
            small[o + 3] = (byte)(asum / count);
        }

        var effect = new WriteableBitmap(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
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

        var rendered = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        rendered.Render(full);
        rendered.Freeze();
        ApplySnapshot(rendered);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAndClose();

    private void CopyAndClose()
    {
        CommitInlineText(save: true);
        try
        {
            var image = RenderAnnotatedImage();
            Clipboard.SetDataObject(new System.Windows.DataObject(System.Windows.DataFormats.Bitmap, image), true);
            WasCopied = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clipboard error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitInlineText(save: true);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"ClipShot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true)
            return;

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
            MessageBox.Show(this, ex.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
