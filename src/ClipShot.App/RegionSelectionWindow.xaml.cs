using Cursors = System.Windows.Input.Cursors;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipShot.Services;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace ClipShot;

public partial class RegionSelectionWindow : Window
{
    private readonly RegionCaptureSession _session;
    private readonly Screen _screen;
    private readonly double _dimOpacity;
    private bool _isSelecting;
    private Rect? _hole;
    private DrawingRectangle? _selection;

    internal RegionSelectionWindow(RegionCaptureSession session, Screen screen, BitmapSource screenshot, double dimOpacity = 0.45)
    {
        InitializeComponent();
        _session = session;
        _screen = screen;
        ScreenshotImage.Source = screenshot;
        _dimOpacity = Math.Clamp(dimOpacity, 0, 0.9);
        ApplyDimStyle();
        DisplayLayout.Attach(this, screen.Bounds);
    }

    private void ApplyDimStyle()
    {
        DimOverlay.Fill = Brushes.Black;
        DimOverlay.Opacity = _dimOpacity;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDimOverlay(null);
        Activate();
        Focus();
    }

    private void Window_ContentRendered(object? sender, EventArgs e) => RefreshDimOverlay(_hole);

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ShowSelection(_selection);

    internal void ShowSelection(DrawingRectangle? virtualPixels)
    {
        _selection = virtualPixels;
        if (virtualPixels is not { } pixels)
        {
            SelectionShade.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            HintBorder.Visibility = Visibility.Visible;
            RefreshDimOverlay(null);
            return;
        }

        HintBorder.Visibility = Visibility.Collapsed;
        var intersection = DrawingRectangle.Intersect(pixels, _screen.Bounds);
        if (intersection.Width < 1 || intersection.Height < 1)
        {
            SelectionShade.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            RefreshDimOverlay(null);
            return;
        }

        Rect bounds;
        if (TryToLocal(new DrawingPoint(intersection.Left, intersection.Top), out var topLeft) &&
            TryToLocal(new DrawingPoint(intersection.Right, intersection.Bottom), out var bottomRight))
        {
            bounds = new Rect(topLeft, bottomRight);
        }
        else
        {
            var width = Math.Max(1, ActualWidth);
            var height = Math.Max(1, ActualHeight);
            bounds = new Rect(
                (intersection.Left - _screen.Bounds.Left) / (double)_screen.Bounds.Width * width,
                (intersection.Top - _screen.Bounds.Top) / (double)_screen.Bounds.Height * height,
                intersection.Width / (double)_screen.Bounds.Width * width,
                intersection.Height / (double)_screen.Bounds.Height * height);
        }

        UpdateChrome(bounds, pixels.Width, pixels.Height);
    }

    private void RefreshDimOverlay(Rect? hole)
    {
        var width = Math.Max(RootGrid.ActualWidth, SelectionCanvas.ActualWidth);
        var height = Math.Max(RootGrid.ActualHeight, SelectionCanvas.ActualHeight);
        if (width < 1 || height < 1)
        {
            width = Math.Max(width, Width > 1 ? Width : _screen.Bounds.Width);
            height = Math.Max(height, Height > 1 ? Height : _screen.Bounds.Height);
        }

        _hole = hole;
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
        _isSelecting = true;
        HintBorder.Visibility = Visibility.Collapsed;
        _session.BeginDrag(ToVirtualPixel(e.GetPosition(SelectionCanvas)));
        SelectionCanvas.CaptureMouse();
        Cursor = Cursors.Cross;
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
            return;

        _session.DragMove(ToVirtualPixel(e.GetPosition(SelectionCanvas)), IsSquareModifier());
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();
        _session.DragEnd(ToVirtualPixel(e.GetPosition(SelectionCanvas)), IsSquareModifier());
    }

    private void UpdateChrome(Rect bounds, int pixelWidth, int pixelHeight)
    {
        RefreshDimOverlay(bounds);

        SelectionShade.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionShade, bounds.Left - 1);
        Canvas.SetTop(SelectionShade, bounds.Top - 1);
        SelectionShade.Width = bounds.Width + 2;
        SelectionShade.Height = bounds.Height + 2;

        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, bounds.Left);
        Canvas.SetTop(SelectionRect, bounds.Top);
        SelectionRect.Width = bounds.Width;
        SelectionRect.Height = bounds.Height;

        SizeText.Text = $"{pixelWidth} × {pixelHeight}";
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

    private DrawingPoint ToVirtualPixel(Point local)
    {
        if (PresentationSource.FromVisual(SelectionCanvas) is not null)
        {
            var screen = SelectionCanvas.PointToScreen(local);
            return new DrawingPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        }

        var width = Math.Max(1, SelectionCanvas.ActualWidth);
        var height = Math.Max(1, SelectionCanvas.ActualHeight);
        var x = _screen.Bounds.Left + local.X / width * _screen.Bounds.Width;
        var y = _screen.Bounds.Top + local.Y / height * _screen.Bounds.Height;
        return new DrawingPoint((int)Math.Round(x), (int)Math.Round(y));
    }

    private bool TryToLocal(DrawingPoint pixel, out Point local)
    {
        local = default;
        if (PresentationSource.FromVisual(SelectionCanvas) is null)
            return false;

        local = SelectionCanvas.PointFromScreen(new Point(pixel.X, pixel.Y));
        return true;
    }

    private static bool IsSquareModifier() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
            return min;
        return Math.Clamp(value, min, max);
    }

    private void SelectionCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _session.Cancel();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _session.Cancel();
            e.Handled = true;
        }
    }
}
