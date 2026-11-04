using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Cursors = System.Windows.Input.Cursors;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ClipShot;

public partial class RegionSelectionWindow : Window
{
    private readonly BitmapSource _screenshot;
    private Point _startPoint;
    private bool _isSelecting;

    public BitmapSource? SelectedImage { get; private set; }
    public bool WasSaved { get; private set; }

    public RegionSelectionWindow(BitmapSource screenshot)
    {
        InitializeComponent();
        _screenshot = screenshot;
        ScreenshotImage.Source = screenshot;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SizeDimFull();
        Activate();
        Focus();
    }

    private void SelectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DimFull.Visibility == Visibility.Visible)
            SizeDimFull();
    }

    private void SizeDimFull()
    {
        DimFull.Width = Math.Max(0, SelectionCanvas.ActualWidth);
        DimFull.Height = Math.Max(0, SelectionCanvas.ActualHeight);
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

        var current = e.GetPosition(SelectionCanvas);
        var bounds = Normalize(_startPoint, current);
        if (bounds.Width < 1 && bounds.Height < 1)
            return;

        UpdateChrome(bounds, committed: false);
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var current = e.GetPosition(SelectionCanvas);
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
        UpdateChrome(bounds, committed: true);
        Cursor = Cursors.Arrow;
    }

    private void ActionsBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateChrome(Rect bounds, bool committed)
    {
        var canvasWidth = SelectionCanvas.ActualWidth;
        var canvasHeight = SelectionCanvas.ActualHeight;

        DimFull.Visibility = Visibility.Collapsed;
        SetRect(DimTop, 0, 0, canvasWidth, Math.Max(0, bounds.Top));
        SetRect(DimLeft, 0, bounds.Top, Math.Max(0, bounds.Left), Math.Max(0, bounds.Height));
        SetRect(DimRight, bounds.Right, bounds.Top, Math.Max(0, canvasWidth - bounds.Right), Math.Max(0, bounds.Height));
        SetRect(DimBottom, 0, bounds.Bottom, canvasWidth, Math.Max(0, canvasHeight - bounds.Bottom));

        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, bounds.Left);
        Canvas.SetTop(SelectionRect, bounds.Top);
        SelectionRect.Width = bounds.Width;
        SelectionRect.Height = bounds.Height;

        var pixels = ToPixelRect(bounds);
        SizeText.Text = $"{pixels.Width} × {pixels.Height}";
        SizeBadge.Visibility = Visibility.Visible;
        ActionsBar.Visibility = committed ? Visibility.Visible : Visibility.Collapsed;
        HintBorder.Visibility = Visibility.Collapsed;
        PositionFloatingChrome(bounds, canvasWidth, canvasHeight, committed);
    }

    private void PositionFloatingChrome(Rect bounds, double canvasWidth, double canvasHeight, bool committed)
    {
        SizeBadge.UpdateLayout();
        var badgeWidth = SizeBadge.ActualWidth;
        var badgeHeight = SizeBadge.ActualHeight;
        var badgeLeft = Clamp(bounds.Left + (bounds.Width - badgeWidth) / 2, 10, canvasWidth - badgeWidth - 10);
        var badgeTop = bounds.Top - badgeHeight - 10;
        var badgeInside = badgeTop < 10;
        if (badgeInside)
            badgeTop = bounds.Top + 10;

        if (committed)
        {
            ActionsBar.UpdateLayout();
            var barWidth = ActionsBar.ActualWidth;
            var barHeight = ActionsBar.ActualHeight;
            var barLeft = Clamp(bounds.Left + (bounds.Width - barWidth) / 2, 10, canvasWidth - barWidth - 10);
            var barTop = bounds.Bottom + 10;
            if (barTop + barHeight > canvasHeight - 10)
                barTop = bounds.Top - barHeight - 10;
            if (barTop < 10)
                barTop = Math.Min(bounds.Top + 10, Math.Max(10, canvasHeight - barHeight - 10));

            var barOverlapsBadge = barTop < badgeTop + badgeHeight && barTop + barHeight > badgeTop;
            if (barOverlapsBadge)
                badgeTop = Clamp(barTop + barHeight + 8, 10, Math.Max(10, canvasHeight - badgeHeight - 10));

            Canvas.SetLeft(ActionsBar, barLeft);
            Canvas.SetTop(ActionsBar, barTop);
        }

        Canvas.SetLeft(SizeBadge, badgeLeft);
        Canvas.SetTop(SizeBadge, badgeTop);
    }

    private void ResetSelectionVisual()
    {
        _isSelecting = false;
        SelectedImage = null;
        WasSaved = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        DimTop.Visibility = Visibility.Collapsed;
        DimLeft.Visibility = Visibility.Collapsed;
        DimRight.Visibility = Visibility.Collapsed;
        DimBottom.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        ActionsBar.Visibility = Visibility.Collapsed;
        HintBorder.Visibility = Visibility.Visible;
        DimFull.Visibility = Visibility.Visible;
        SizeDimFull();
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

    private static Rect Normalize(Point start, Point current)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        return new Rect(left, top, Math.Abs(current.X - start.X), Math.Abs(current.Y - start.Y));
    }

    private static void SetRect(Rectangle rectangle, double x, double y, double width, double height)
    {
        rectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
            return min;
        return Math.Clamp(value, min, max);
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedImage is not null)
            DialogResult = true;
    }

    private void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedImage is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save selected region",
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
            encoder.Frames.Add(BitmapFrame.Create(SelectedImage));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            WasSaved = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"ClipShot couldn't save the selected region.\n\n{ex.Message}",
                "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelSelection_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
            return;
        }

        if (SelectedImage is null)
            return;

        if (e.Key == Key.Enter)
        {
            CopySelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveSelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
