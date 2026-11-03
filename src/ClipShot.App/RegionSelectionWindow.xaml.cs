using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipShot;

public partial class RegionSelectionWindow : Window
{
    private readonly BitmapSource _screenshot;
    private Point _startPoint;
    private bool _isSelecting;

    public BitmapSource? SelectedImage { get; private set; }

    public RegionSelectionWindow(BitmapSource screenshot)
    {
        InitializeComponent();
        _screenshot = screenshot;
        ScreenshotImage.Source = screenshot;
    }

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        _isSelecting = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionCanvas.CaptureMouse();
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
            return;

        var current = e.GetPosition(SelectionCanvas);
        var left = Math.Min(_startPoint.X, current.X);
        var top = Math.Min(_startPoint.Y, current.Y);
        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = Math.Abs(current.X - _startPoint.X);
        SelectionRect.Height = Math.Abs(current.Y - _startPoint.Y);
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var current = e.GetPosition(SelectionCanvas);
        var left = Math.Min(_startPoint.X, current.X);
        var top = Math.Min(_startPoint.Y, current.Y);
        var width = Math.Abs(current.X - _startPoint.X);
        var height = Math.Abs(current.Y - _startPoint.Y);

        if (width < 3 || height < 3)
        {
            DialogResult = false;
            return;
        }

        var scaleX = _screenshot.PixelWidth / Math.Max(1, SelectionCanvas.ActualWidth);
        var scaleY = _screenshot.PixelHeight / Math.Max(1, SelectionCanvas.ActualHeight);
        var x = Math.Clamp((int)Math.Floor(left * scaleX), 0, _screenshot.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(top * scaleY), 0, _screenshot.PixelHeight - 1);
        var cropWidth = Math.Clamp((int)Math.Ceiling(width * scaleX), 1, _screenshot.PixelWidth - x);
        var cropHeight = Math.Clamp((int)Math.Ceiling(height * scaleY), 1, _screenshot.PixelHeight - y);

        var cropped = new CroppedBitmap(_screenshot, new Int32Rect(x, y, cropWidth, cropHeight));
        cropped.Freeze();
        SelectedImage = cropped;
        DialogResult = true;
    }

    private void SelectionCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
