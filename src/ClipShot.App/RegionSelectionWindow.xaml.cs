using Cursors = System.Windows.Input.Cursors;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace ClipShot;

public partial class RegionSelectionWindow : Window
{
    private readonly BitmapSource _screenshot;
    private readonly double _dimOpacity;
    private Point _startPoint;
    private bool _isSelecting;
    private Rect? _selectionBounds;

    public BitmapSource FullScreenshot => _screenshot;
    public BitmapSource? SelectedImage { get; private set; }
    public Rect SelectionBounds { get; private set; }
    public Int32Rect SelectionPixels { get; private set; }

    public RegionSelectionWindow(BitmapSource screenshot, double dimOpacity = 0.45)
    {
        InitializeComponent();
        _screenshot = screenshot;
        ScreenshotImage.Source = screenshot;
        _dimOpacity = Math.Clamp(dimOpacity, 0, 0.9);
        ApplyDimStyle();
    }

    private void ApplyDimStyle()
    {
        // Solid black + Opacity is more reliable than baking alpha into the brush.
        DimOverlay.Fill = Brushes.Black;
        DimOverlay.Opacity = _dimOpacity;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDimOverlay(null);
        Activate();
        Focus();
    }

    private void Window_ContentRendered(object? sender, EventArgs e) => RefreshDimOverlay(_selectionBounds);

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshDimOverlay(_selectionBounds);

    private void RefreshDimOverlay(Rect? hole)
    {
        var width = Math.Max(RootGrid.ActualWidth, SelectionCanvas.ActualWidth);
        var height = Math.Max(RootGrid.ActualHeight, SelectionCanvas.ActualHeight);
        if (width < 1 || height < 1)
        {
            width = Math.Max(width, SystemParameters.PrimaryScreenWidth);
            height = Math.Max(height, SystemParameters.PrimaryScreenHeight);
        }

        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
        if (hole is { Width: > 0, Height: > 0 } selection)
            geometry.Children.Add(new RectangleGeometry(selection));

        geometry.Freeze();
        DimOverlay.Data = geometry;
        ApplyDimStyle();
    }

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ResetSelectionVisual();
        _startPoint = e.GetPosition(SelectionCanvas);
        _isSelecting = true;
        HintBorder.Visibility = Visibility.Collapsed;
        SelectionCanvas.CaptureMouse();
        Cursor = Cursors.Cross;
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
            return;

        var current = ConstrainSelectionPoint(e.GetPosition(SelectionCanvas));
        var bounds = Normalize(_startPoint, current);
        if (bounds.Width < 1 && bounds.Height < 1)
            return;

        UpdateChrome(bounds);
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var current = ConstrainSelectionPoint(e.GetPosition(SelectionCanvas));
        var bounds = Normalize(_startPoint, current);
        if (bounds.Width < 3 || bounds.Height < 3)
        {
            ResetSelectionVisual();
            return;
        }

        var pixels = ToPixelRect(bounds);
        var cropped = new CroppedBitmap(_screenshot, pixels);
        cropped.Freeze();
        SelectedImage = cropped;
        SelectionBounds = bounds;
        SelectionPixels = pixels;
        DialogResult = true;
    }

    private void UpdateChrome(Rect bounds)
    {
        _selectionBounds = bounds;
        RefreshDimOverlay(bounds);

        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, bounds.Left);
        Canvas.SetTop(SelectionRect, bounds.Top);
        SelectionRect.Width = bounds.Width;
        SelectionRect.Height = bounds.Height;

        var pixels = ToPixelRect(bounds);
        SizeText.Text = $"{pixels.Width} × {pixels.Height}";
        SizeBadge.Visibility = Visibility.Visible;
        HintBorder.Visibility = Visibility.Collapsed;
        PositionSizeBadge(bounds);
    }

    private void PositionSizeBadge(Rect bounds)
    {
        SizeBadge.UpdateLayout();
        var canvasWidth = Math.Max(SelectionCanvas.ActualWidth, RootGrid.ActualWidth);
        var canvasHeight = Math.Max(SelectionCanvas.ActualHeight, RootGrid.ActualHeight);
        var badgeWidth = SizeBadge.ActualWidth;
        var badgeHeight = SizeBadge.ActualHeight;
        var badgeLeft = Clamp(bounds.Left + (bounds.Width - badgeWidth) / 2, 10, canvasWidth - badgeWidth - 10);
        var badgeTop = bounds.Top - badgeHeight - 10;
        if (badgeTop < 10)
            badgeTop = Clamp(bounds.Top + 10, 10, Math.Max(10, canvasHeight - badgeHeight - 10));

        Canvas.SetLeft(SizeBadge, badgeLeft);
        Canvas.SetTop(SizeBadge, badgeTop);
    }

    private void ResetSelectionVisual()
    {
        _isSelecting = false;
        _selectionBounds = null;
        SelectedImage = null;
        SelectionBounds = default;
        SelectionPixels = default;
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SizeBadge.Visibility = Visibility.Collapsed;
        HintBorder.Visibility = Visibility.Visible;
        RefreshDimOverlay(null);
        Cursor = Cursors.Cross;
    }

    private Int32Rect ToPixelRect(Rect bounds)
    {
        var scaleX = _screenshot.PixelWidth / Math.Max(1, SelectionCanvas.ActualWidth);
        var scaleY = _screenshot.PixelHeight / Math.Max(1, SelectionCanvas.ActualHeight);
        var x = Math.Clamp((int)Math.Floor(bounds.Left * scaleX), 0, Math.Max(0, _screenshot.PixelWidth - 1));
        var y = Math.Clamp((int)Math.Floor(bounds.Top * scaleY), 0, Math.Max(0, _screenshot.PixelHeight - 1));
        var cropWidth = Math.Clamp((int)Math.Ceiling(bounds.Width * scaleX), 1, _screenshot.PixelWidth - x);
        var cropHeight = Math.Clamp((int)Math.Ceiling(bounds.Height * scaleY), 1, _screenshot.PixelHeight - y);
        return new Int32Rect(x, y, cropWidth, cropHeight);
    }

    private Point ConstrainSelectionPoint(Point current)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            return current;

        var dx = current.X - _startPoint.X;
        var dy = current.Y - _startPoint.Y;
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(
            _startPoint.X + Math.Sign(dx == 0 ? 1 : dx) * side,
            _startPoint.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
    }

    private static Rect Normalize(Point start, Point current)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        return new Rect(left, top, Math.Abs(current.X - start.X), Math.Abs(current.Y - start.Y));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
            return min;
        return Math.Clamp(value, min, max);
    }

    private void SelectionCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
