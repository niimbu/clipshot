using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using ColorConverter = System.Windows.Media.ColorConverter;

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
    private UIElement? _selectedElement;
    private Stroke? _selectedStroke;
    private Rectangle? _selectionChrome;
    private bool _selectionMoving;
    private Point _selectionDragStart;
    private double _selectionOriginLeft;
    private double _selectionOriginTop;
    private StylusPointCollection? _selectionStrokeOrigin;
    private Color _annotationColor = Color.FromRgb(0xE1, 0x1D, 0x48);
    private bool _inkStraightMode;
    private readonly bool _inPlaceMode;
    private readonly BitmapSource? _fullScreenshot;
    private Rect _regionBounds;
    private double _dimOpacity = 0.45;

    public bool WasCopied { get; private set; }
    public bool WasSaved { get; private set; }

    public ScreenshotEditorWindow(BitmapSource image)
    {
        InitializeComponent();
        ConfigureSurface(image);
        SetAnnotationColor(_annotationColor);
        SetTool(EditorTool.Pen);
    }

    public ScreenshotEditorWindow(BitmapSource regionImage, BitmapSource fullScreenshot, Rect regionBounds, double dimOpacity)
    {
        _inPlaceMode = true;
        _fullScreenshot = fullScreenshot;
        _regionBounds = regionBounds;
        _dimOpacity = Math.Clamp(dimOpacity, 0, 0.9);
        InitializeComponent();
        ConfigureSurface(regionImage);
        EnableInPlaceMode();
        SetAnnotationColor(_annotationColor);
        SetTool(EditorTool.Pen);
    }

    private void ConfigureSurface(BitmapSource image)
    {
        PreviewImage.Source = image;
        var width = image.PixelWidth * 96d / image.DpiX;
        var height = image.PixelHeight * 96d / image.DpiY;
        AnnotationCanvas.Width = width;
        AnnotationCanvas.Height = height;
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        PreviewSurface.Width = width;
        PreviewSurface.Height = height;
    }

    private void EnableInPlaceMode()
    {
        Background = Brushes.Black;
        CenteredHost.Visibility = Visibility.Collapsed;
        InPlaceHost.Visibility = Visibility.Visible;
        FullScreenImage.Source = _fullScreenshot;

        if (CenteredSurfaceBorder.Child == PreviewSurface)
            CenteredSurfaceBorder.Child = null;
        InPlaceSurfaceSlot.Children.Clear();
        InPlaceSurfaceSlot.Children.Add(PreviewSurface);

        InPlaceDimOverlay.Fill = Brushes.Black;
        InPlaceDimOverlay.Opacity = _dimOpacity;
        UpdateInPlaceLayout();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_inPlaceMode)
            UpdateInPlaceLayout();
        Activate();
        Focus();
        AnnotationCanvas.Focus();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_inPlaceMode)
            UpdateInPlaceLayout();
    }

    private void SetTool(EditorTool tool)
    {
        CommitInlineText(save: true);
        if (tool != EditorTool.Select)
            ClearAnnotationSelection();

        _tool = tool;
        _inkStraightMode = false;
        // Use None for Select — InkCanvas's built-in Select mode fights our Preview mouse
        // handlers and fails on Line/Path children with zero layout size. Custom hit-test
        // selection below handles both shapes and ink strokes.
        AnnotationCanvas.EditingMode = tool switch
        {
            EditorTool.Pen or EditorTool.Highlight => InkCanvasEditingMode.Ink,
            EditorTool.Erase => InkCanvasEditingMode.EraseByPoint,
            _ => InkCanvasEditingMode.None
        };
        if (tool == EditorTool.Erase)
            AnnotationCanvas.EraserShape = new EllipseStylusShape(22, 22);

        AnnotationCanvas.Cursor = tool switch
        {
            EditorTool.Blur or EditorTool.Pixelate or EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Arrow or EditorTool.Line => Cursors.Cross,
            EditorTool.Erase => Cursors.Cross,
            EditorTool.Select => Cursors.Hand,
            EditorTool.Text or EditorTool.Number => Cursors.IBeam,
            _ => Cursors.Pen
        };
        ApplyDrawingAttributes();
        UpdateActiveToolButton();
    }

    private void ApplyDrawingAttributes()
    {
        var color = _tool == EditorTool.Highlight
            ? Color.FromArgb(120, _annotationColor.R, _annotationColor.G, _annotationColor.B)
            : _annotationColor;
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = _tool == EditorTool.Highlight ? 22 : 3,
            Height = _tool == EditorTool.Highlight ? 22 : 3,
            FitToCurve = true,
            IgnorePressure = true,
            IsHighlighter = _tool == EditorTool.Highlight
        };
    }

    private void SetAnnotationColor(Color color)
    {
        _annotationColor = color;
        ApplyDrawingAttributes();
        UpdateColorSwatches();
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || string.IsNullOrWhiteSpace(button.Uid))
            return;
        try
        {
            if (ColorConverter.ConvertFromString(button.Uid) is Color color)
                SetAnnotationColor(color);
        }
        catch
        {
            // Ignore invalid palette colors.
        }
    }

    private void UpdateColorSwatches()
    {
        void sync(Button button, string hex)
        {
            button.Uid = hex;
            var active = false;
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color)
                    active = color.R == _annotationColor.R && color.G == _annotationColor.G && color.B == _annotationColor.B;
            }
            catch
            {
                active = false;
            }

            button.Tag = active ? "Active" : null;
            button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }

        sync(ColorRedButton, "#FFE11D48");
        sync(ColorOrangeButton, "#FFF97316");
        sync(ColorYellowButton, "#FFFACC15");
        sync(ColorGreenButton, "#FF22C55E");
        sync(ColorBlueButton, "#FF3B82F6");
        sync(ColorPurpleButton, "#FFA855F7");
        sync(ColorWhiteButton, "#FFF8FAFC");
        sync(ColorBlackButton, "#FF0F172A");
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
        // While typing a text annotation, never steal keys (especially Space → copy/close).
        if (IsEditingTextAnnotation())
            return;

        if (e.Key == Key.Escape)
        {
            if (_selectedElement is not null || _selectedStroke is not null)
            {
                ClearAnnotationSelection();
                e.Handled = true;
                return;
            }

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

    private bool IsEditingTextAnnotation() =>
        _inlineTextHost is not null || Keyboard.FocusedElement is TextBox;

    private void DeleteSelection_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void DeleteSelection()
    {
        CommitInlineText(save: false);

        if (_selectedElement is not null || _selectedStroke is not null)
        {
            SaveUndoPoint();
            if (_selectedElement is not null)
                AnnotationCanvas.Children.Remove(_selectedElement);
            if (_selectedStroke is not null)
                AnnotationCanvas.Strokes.Remove(_selectedStroke);
            ClearAnnotationSelection();
            return;
        }

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

        // Selection chrome is editor-only UI — never bake it into copies/undos.
        var chromeVisibility = _selectionChrome?.Visibility;
        if (_selectionChrome is not null)
            _selectionChrome.Visibility = Visibility.Collapsed;

        try
        {
            PreviewSurface.Measure(new Size(dipWidth, dipHeight));
            PreviewSurface.Arrange(new Rect(0, 0, dipWidth, dipHeight));
            PreviewSurface.UpdateLayout();

            var rendered = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            rendered.Render(PreviewSurface);
            rendered.Freeze();
            return rendered;
        }
        finally
        {
            if (_selectionChrome is not null && chromeVisibility is not null)
                _selectionChrome.Visibility = chromeVisibility.Value;
        }
    }

    private void SaveUndoPoint()
    {
        _undo.Push(RenderAnnotatedImage());
        _redo.Clear();
    }

    private void ApplySnapshot(BitmapSource snapshot)
    {
        ClearAnnotationSelection();
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
        if (_inPlaceMode)
        {
            _regionBounds = new Rect(_regionBounds.Left, _regionBounds.Top, displayWidth, displayHeight);
            UpdateInPlaceLayout();
        }
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
        if (_tool is not (EditorTool.Pen or EditorTool.Highlight))
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            return;

        var points = e.Stroke.StylusPoints;
        if (points.Count < 2)
            return;

        var start = new Point(points[0].X, points[0].Y);
        var end = SnapPointToAxisOrDiagonal(start, new Point(points[^1].X, points[^1].Y));
        e.Stroke.StylusPoints = new StylusPointCollection
        {
            new StylusPoint(start.X, start.Y),
            new StylusPoint(end.X, end.Y)
        };
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
            _start = position;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Straight ink: suppress freehand and draw a constrained stroke preview.
                _inkStraightMode = true;
                AnnotationCanvas.EditingMode = InkCanvasEditingMode.None;
                var brush = new SolidColorBrush(GetStrokeColor());
                brush.Freeze();
                _previewShape = new Line
                {
                    Stroke = brush,
                    StrokeThickness = _tool == EditorTool.Highlight ? 22 : 3,
                    Opacity = _tool == EditorTool.Highlight ? 0.55 : 1,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                AnnotationCanvas.Children.Add(_previewShape);
                UpdateShape(ConstrainDrawingPoint(position));
                AnnotationCanvas.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                _inkStraightMode = false;
            }

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
        {
            BeginSelectionAt(position);
            e.Handled = true;
            return;
        }

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
                Background = CreateAnnotationBrush(),
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
            UpdateShape(ConstrainDrawingPoint(position));
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
            UpdateShape(ConstrainDrawingPoint(position));
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(AnnotationCanvas);
        if (_tool == EditorTool.Select && _selectionMoving)
        {
            MoveSelectionTo(position);
            e.Handled = true;
            return;
        }

        if (!_drawing)
            return;

        if (_tool == EditorTool.Erase)
        {
            EraseElementsAt(position);
            e.Handled = false;
        }
        else if (_previewShape is not null)
        {
            UpdateShape(ConstrainDrawingPoint(position));
            e.Handled = true;
        }
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tool == EditorTool.Select && _selectionMoving)
        {
            EndSelectionMove();
            e.Handled = true;
            return;
        }

        if (!_drawing)
            return;

        if (_tool is EditorTool.Pen or EditorTool.Highlight)
        {
            if (_inkStraightMode && _previewShape is Line preview)
            {
                var straightEnd = ConstrainDrawingPoint(e.GetPosition(AnnotationCanvas));
                UpdateShape(straightEnd);
                AnnotationCanvas.Children.Remove(preview);
                _previewShape = null;
                AnnotationCanvas.ReleaseMouseCapture();
                _inkStraightMode = false;
                _drawing = false;
                e.Handled = true;

                if (Math.Abs(straightEnd.X - _start.X) >= 2 || Math.Abs(straightEnd.Y - _start.Y) >= 2)
                {
                    if (_beforeAction is not null)
                        SaveUndoPointFrom(_beforeAction);
                    var attrs = AnnotationCanvas.DefaultDrawingAttributes.Clone();
                    var stroke = new Stroke(new StylusPointCollection
                    {
                        new StylusPoint(_start.X, _start.Y),
                        new StylusPoint(straightEnd.X, straightEnd.Y)
                    }, attrs);
                    AnnotationCanvas.Strokes.Add(stroke);
                }

                AnnotationCanvas.EditingMode = InkCanvasEditingMode.Ink;
                return;
            }

            _drawing = false;
            _inkStraightMode = false;
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
        var end = ConstrainDrawingPoint(e.GetPosition(AnnotationCanvas));
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

    private Point ConstrainDrawingPoint(Point current)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift && !_inkStraightMode)
            return current;

        return _tool switch
        {
            EditorTool.Line or EditorTool.Arrow or EditorTool.Pen or EditorTool.Highlight
                => SnapPointToAxisOrDiagonal(_start, current),
            EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Blur or EditorTool.Pixelate
                => SnapPointToSquare(_start, current),
            _ => current
        };
    }

    private Color GetStrokeColor() =>
        _tool == EditorTool.Highlight
            ? Color.FromArgb(120, _annotationColor.R, _annotationColor.G, _annotationColor.B)
            : _annotationColor;

    private SolidColorBrush CreateAnnotationBrush()
    {
        var brush = new SolidColorBrush(_annotationColor);
        brush.Freeze();
        return brush;
    }

    private static Point SnapPointToAxisOrDiagonal(Point start, Point current)
    {
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        var absX = Math.Abs(dx);
        var absY = Math.Abs(dy);
        if (absX < 0.001 && absY < 0.001)
            return current;

        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        var snapped = Math.Round(angle / 45d) * 45d;
        var radians = snapped * Math.PI / 180d;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new Point(start.X + Math.Cos(radians) * length, start.Y + Math.Sin(radians) * length);
    }

    private static Point SnapPointToSquare(Point start, Point current)
    {
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(
            start.X + Math.Sign(dx == 0 ? 1 : dx) * side,
            start.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
    }

    private Shape CreateShape(EditorTool tool)
    {
        var brush = CreateAnnotationBrush();
        return tool switch
        {
            EditorTool.Line => new Line { Stroke = brush, StrokeThickness = 3 },
            EditorTool.Arrow => new Path
            {
                Stroke = brush,
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = brush
            },
            EditorTool.Ellipse => new Ellipse { Stroke = brush, StrokeThickness = 3, Fill = Brushes.Transparent },
            _ => new Rectangle { Stroke = brush, StrokeThickness = 3, Fill = Brushes.Transparent }
        };
    }

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
            .Where(element => element != _selectionChrome)
            .Where(element => element is Shape or TextBlock or Border)
            .Where(element => HitTestAnnotation(element, point, radius))
            .ToArray();

        foreach (var element in hitElements)
        {
            if (element == _selectedElement)
                ClearAnnotationSelection();
            AnnotationCanvas.Children.Remove(element);
            _eraseChanged = true;
        }
    }

    private void BeginSelectionAt(Point position)
    {
        var element = HitTestTopAnnotation(position);
        if (element is not null)
        {
            SelectAnnotationElement(element);
            StartSelectionMove(position);
            return;
        }

        var strokes = AnnotationCanvas.Strokes.HitTest(position, 10);
        if (strokes.Count > 0)
        {
            // Prefer the top-most (last drawn) stroke under the pointer.
            SelectAnnotationStroke(strokes[^1]);
            StartSelectionMove(position);
            return;
        }

        ClearAnnotationSelection();
    }

    private void StartSelectionMove(Point position)
    {
        _selectionMoving = true;
        _selectionDragStart = position;
        _beforeAction = RenderAnnotatedImage();

        if (_selectedElement is not null)
        {
            _selectionOriginLeft = InkCanvas.GetLeft(_selectedElement);
            _selectionOriginTop = InkCanvas.GetTop(_selectedElement);
            if (double.IsNaN(_selectionOriginLeft)) _selectionOriginLeft = 0;
            if (double.IsNaN(_selectionOriginTop)) _selectionOriginTop = 0;
        }
        else if (_selectedStroke is not null)
        {
            _selectionStrokeOrigin = new StylusPointCollection(_selectedStroke.StylusPoints);
        }

        AnnotationCanvas.CaptureMouse();
    }

    private void MoveSelectionTo(Point position)
    {
        var dx = position.X - _selectionDragStart.X;
        var dy = position.Y - _selectionDragStart.Y;

        if (_selectedElement is not null)
        {
            PlaceElement(_selectedElement, _selectionOriginLeft + dx, _selectionOriginTop + dy);
            UpdateSelectionChrome();
            return;
        }

        if (_selectedStroke is not null && _selectionStrokeOrigin is not null)
        {
            var moved = new StylusPointCollection(_selectionStrokeOrigin.Count);
            foreach (var point in _selectionStrokeOrigin)
                moved.Add(new StylusPoint(point.X + dx, point.Y + dy, point.PressureFactor));
            _selectedStroke.StylusPoints = moved;
            UpdateSelectionChrome();
        }
    }

    private void EndSelectionMove()
    {
        if (!_selectionMoving)
            return;

        _selectionMoving = false;
        AnnotationCanvas.ReleaseMouseCapture();

        var moved = false;
        if (_selectedElement is not null)
        {
            var left = InkCanvas.GetLeft(_selectedElement);
            var top = InkCanvas.GetTop(_selectedElement);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            moved = Math.Abs(left - _selectionOriginLeft) > 0.5 || Math.Abs(top - _selectionOriginTop) > 0.5;
        }
        else if (_selectedStroke is not null && _selectionStrokeOrigin is not null && _selectedStroke.StylusPoints.Count > 0)
        {
            var first = _selectedStroke.StylusPoints[0];
            var origin = _selectionStrokeOrigin[0];
            moved = Math.Abs(first.X - origin.X) > 0.5 || Math.Abs(first.Y - origin.Y) > 0.5;
        }

        if (moved && _beforeAction is not null)
            SaveUndoPointFrom(_beforeAction);

        _selectionStrokeOrigin = null;
        _beforeAction = null;
    }

    private void SelectAnnotationElement(UIElement element)
    {
        _selectedStroke = null;
        _selectedElement = element;
        UpdateSelectionChrome();
    }

    private void SelectAnnotationStroke(Stroke stroke)
    {
        _selectedElement = null;
        _selectedStroke = stroke;
        UpdateSelectionChrome();
    }

    private void ClearAnnotationSelection()
    {
        if (_selectionMoving && AnnotationCanvas.IsMouseCaptured)
            AnnotationCanvas.ReleaseMouseCapture();

        _selectionMoving = false;
        _selectedElement = null;
        _selectedStroke = null;
        _selectionStrokeOrigin = null;
        if (_selectionChrome is not null)
        {
            AnnotationCanvas.Children.Remove(_selectionChrome);
            _selectionChrome = null;
        }
    }

    private void UpdateSelectionChrome()
    {
        var bounds = GetSelectionBounds();
        if (bounds is null)
        {
            if (_selectionChrome is not null)
            {
                AnnotationCanvas.Children.Remove(_selectionChrome);
                _selectionChrome = null;
            }
            return;
        }

        var rect = bounds.Value;
        rect.Inflate(4, 4);

        if (_selectionChrome is null)
        {
            _selectionChrome = new Rectangle
            {
                Stroke = Brushes.White,
                StrokeThickness = 1.25,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            AnnotationCanvas.Children.Add(_selectionChrome);
        }

        PlaceElement(_selectionChrome, rect.Left, rect.Top);
        _selectionChrome.Width = Math.Max(1, rect.Width);
        _selectionChrome.Height = Math.Max(1, rect.Height);

        // Keep chrome above the selection target.
        AnnotationCanvas.Children.Remove(_selectionChrome);
        AnnotationCanvas.Children.Add(_selectionChrome);
    }

    private Rect? GetSelectionBounds()
    {
        if (_selectedElement is not null)
            return GetAnnotationBounds(_selectedElement);

        if (_selectedStroke is not null)
        {
            var bounds = _selectedStroke.GetBounds();
            if (bounds.IsEmpty)
                return null;
            return bounds;
        }

        return null;
    }

    private UIElement? HitTestTopAnnotation(Point point)
    {
        const double radius = 6;
        return AnnotationCanvas.Children.Cast<UIElement>()
            .Where(element => element is not Border host || host != _inlineTextHost)
            .Where(element => element != _selectionChrome)
            .Where(element => element is Shape or TextBlock or Border)
            .Reverse()
            .FirstOrDefault(element => HitTestAnnotation(element, point, radius));
    }

    private static Rect GetAnnotationBounds(UIElement element)
    {
        var left = InkCanvas.GetLeft(element);
        var top = InkCanvas.GetTop(element);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        Rect local;
        if (element is Line line)
        {
            local = new Rect(
                Math.Min(line.X1, line.X2),
                Math.Min(line.Y1, line.Y2),
                Math.Max(1, Math.Abs(line.X2 - line.X1)),
                Math.Max(1, Math.Abs(line.Y2 - line.Y1)));
        }
        else if (element is Shape shape)
        {
            var geometryBounds = shape.RenderedGeometry.Bounds;
            if (geometryBounds.IsEmpty)
                local = new Rect(0, 0, Math.Max(1, shape.ActualWidth > 0 ? shape.ActualWidth : shape.Width),
                    Math.Max(1, shape.ActualHeight > 0 ? shape.ActualHeight : shape.Height));
            else
                local = geometryBounds;
        }
        else
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = element.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
                size = element.RenderSize;
            local = new Rect(0, 0, Math.Max(size.Width, 8), Math.Max(size.Height, 8));
        }

        return new Rect(left + local.Left, top + local.Top, Math.Max(local.Width, 1), Math.Max(local.Height, 1));
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

        var brush = CreateAnnotationBrush();
        _inlineTextBox = new TextBox
        {
            MinWidth = 140,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = brush,
            Padding = new Thickness(2, 0, 2, 0)
        };
        _inlineTextBox.KeyDown += InlineTextBox_KeyDown;
        _inlineTextBox.LostKeyboardFocus += (_, _) => CommitInlineText(save: true);

        _inlineTextHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderBrush = brush,
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
            Foreground = CreateAnnotationBrush(),
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

    private void UpdateInPlaceLayout()
    {
        if (!_inPlaceMode || _fullScreenshot is null)
            return;

        var hostWidth = Math.Max(ActualWidth, SystemParameters.PrimaryScreenWidth);
        var hostHeight = Math.Max(ActualHeight, SystemParameters.PrimaryScreenHeight);
        _regionBounds = ClampRegionBounds(_regionBounds, hostWidth, hostHeight);

        Canvas.SetLeft(RegionChrome, _regionBounds.Left);
        Canvas.SetTop(RegionChrome, _regionBounds.Top);
        RegionChrome.Width = _regionBounds.Width;
        RegionChrome.Height = _regionBounds.Height;

        PreviewSurface.Width = _regionBounds.Width;
        PreviewSurface.Height = _regionBounds.Height;
        PreviewImage.Width = _regionBounds.Width;
        PreviewImage.Height = _regionBounds.Height;
        AnnotationCanvas.Width = _regionBounds.Width;
        AnnotationCanvas.Height = _regionBounds.Height;

        RefreshRegionCrop();
        RefreshInPlaceDim(hostWidth, hostHeight);
        PositionResizeHandles();
        PositionInPlaceSizeBadge();
    }

    private void RefreshRegionCrop()
    {
        if (_fullScreenshot is null)
            return;

        var pixels = ToPixelRect(_regionBounds, _fullScreenshot, ActualWidth > 1 ? ActualWidth : SystemParameters.PrimaryScreenWidth,
            ActualHeight > 1 ? ActualHeight : SystemParameters.PrimaryScreenHeight);
        if (pixels.Width < 1 || pixels.Height < 1)
            return;

        var cropped = new CroppedBitmap(_fullScreenshot, pixels);
        cropped.Freeze();
        PreviewImage.Source = cropped;
        InPlaceSizeText.Text = $"{pixels.Width} × {pixels.Height}";
    }

    private void RefreshInPlaceDim(double width, double height)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
        if (_regionBounds.Width > 0 && _regionBounds.Height > 0)
            geometry.Children.Add(new RectangleGeometry(_regionBounds));
        geometry.Freeze();
        InPlaceDimOverlay.Data = geometry;
        InPlaceDimOverlay.Fill = Brushes.Black;
        InPlaceDimOverlay.Opacity = _dimOpacity;
    }

    private void PositionResizeHandles()
    {
        void place(Thumb handle, double x, double y)
        {
            Canvas.SetLeft(handle, x - handle.Width / 2);
            Canvas.SetTop(handle, y - handle.Height / 2);
        }

        var r = _regionBounds;
        place(HandleNW, r.Left, r.Top);
        place(HandleN, r.Left + r.Width / 2, r.Top);
        place(HandleNE, r.Right, r.Top);
        place(HandleE, r.Right, r.Top + r.Height / 2);
        place(HandleSE, r.Right, r.Bottom);
        place(HandleS, r.Left + r.Width / 2, r.Bottom);
        place(HandleSW, r.Left, r.Bottom);
        place(HandleW, r.Left, r.Top + r.Height / 2);
    }

    private void PositionInPlaceSizeBadge()
    {
        InPlaceSizeBadge.UpdateLayout();
        var badgeWidth = Math.Max(InPlaceSizeBadge.ActualWidth, 64);
        var badgeHeight = Math.Max(InPlaceSizeBadge.ActualHeight, 24);
        var left = _regionBounds.Left + (_regionBounds.Width - badgeWidth) / 2;
        var top = _regionBounds.Top - badgeHeight - 8;
        if (top < 8)
            top = _regionBounds.Top + 8;
        Canvas.SetLeft(InPlaceSizeBadge, Math.Max(8, left));
        Canvas.SetTop(InPlaceSizeBadge, top);
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_inPlaceMode || sender is not Thumb handle || handle.Tag is not string edge)
            return;

        var left = _regionBounds.Left;
        var top = _regionBounds.Top;
        var right = _regionBounds.Right;
        var bottom = _regionBounds.Bottom;
        const double minSize = 24;

        switch (edge)
        {
            case "NW":
                left += e.HorizontalChange;
                top += e.VerticalChange;
                break;
            case "N":
                top += e.VerticalChange;
                break;
            case "NE":
                right += e.HorizontalChange;
                top += e.VerticalChange;
                break;
            case "E":
                right += e.HorizontalChange;
                break;
            case "SE":
                right += e.HorizontalChange;
                bottom += e.VerticalChange;
                break;
            case "S":
                bottom += e.VerticalChange;
                break;
            case "SW":
                left += e.HorizontalChange;
                bottom += e.VerticalChange;
                break;
            case "W":
                left += e.HorizontalChange;
                break;
        }

        if (right - left < minSize)
        {
            if (edge is "E" or "NE" or "SE")
                right = left + minSize;
            else
                left = right - minSize;
        }

        if (bottom - top < minSize)
        {
            if (edge is "S" or "SE" or "SW")
                bottom = top + minSize;
            else
                top = bottom - minSize;
        }

        var hostWidth = Math.Max(ActualWidth, SystemParameters.PrimaryScreenWidth);
        var hostHeight = Math.Max(ActualHeight, SystemParameters.PrimaryScreenHeight);
        _regionBounds = ClampRegionBounds(new Rect(left, top, right - left, bottom - top), hostWidth, hostHeight);

        // Resizing replaces the crop; clear annotations so coordinates stay coherent.
        if (AnnotationCanvas.Strokes.Count > 0 || AnnotationCanvas.Children.Count > 0)
            SaveUndoPoint();
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
        ClearAnnotationSelection();
        UpdateInPlaceLayout();
    }

    private static Rect ClampRegionBounds(Rect bounds, double hostWidth, double hostHeight)
    {
        var width = Math.Clamp(bounds.Width, 24, Math.Max(24, hostWidth));
        var height = Math.Clamp(bounds.Height, 24, Math.Max(24, hostHeight));
        var left = Math.Clamp(bounds.Left, 0, Math.Max(0, hostWidth - width));
        var top = Math.Clamp(bounds.Top, 0, Math.Max(0, hostHeight - height));
        return new Rect(left, top, width, height);
    }

    private static Int32Rect ToPixelRect(Rect bounds, BitmapSource screenshot, double hostWidth, double hostHeight)
    {
        var scaleX = screenshot.PixelWidth / Math.Max(1, hostWidth);
        var scaleY = screenshot.PixelHeight / Math.Max(1, hostHeight);
        var x = Math.Clamp((int)Math.Floor(bounds.Left * scaleX), 0, Math.Max(0, screenshot.PixelWidth - 1));
        var y = Math.Clamp((int)Math.Floor(bounds.Top * scaleY), 0, Math.Max(0, screenshot.PixelHeight - 1));
        var cropWidth = Math.Clamp((int)Math.Ceiling(bounds.Width * scaleX), 1, screenshot.PixelWidth - x);
        var cropHeight = Math.Clamp((int)Math.Ceiling(bounds.Height * scaleY), 1, screenshot.PixelHeight - y);
        return new Int32Rect(x, y, cropWidth, cropHeight);
    }
}
