using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Cursors = System.Windows.Input.Cursors;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ActionsBar.Visibility == Visibility.Visible)
            return;

        SelectionCanvas.Focus();
        _startPoint = e.GetPosition(SelectionCanvas);
        _isSelecting = true;
        SelectionRect.Visibility = Visibility.Visible;
        DimOverlay.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        UpdateDimOverlay(_startPoint, _startPoint);
        SelectionCanvas.CaptureMouse();
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
            return;

        var current = e.GetPosition(SelectionCanvas);
        UpdateSelectionVisuals(current);
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var current = e.GetPosition(SelectionCanvas);
        UpdateSelectionVisuals(current);

        var left = Math.Min(_startPoint.X, current.X);
        var top = Math.Min(_startPoint.Y, current.Y);
        var width = Math.Abs(current.X - _startPoint.X);
        var height = Math.Abs(current.Y - _startPoint.Y);

        if (width < 3 || height < 3)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            DimOverlay.Visibility = Visibility.Collapsed;
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
        HintBorder.Visibility = Visibility.Collapsed;
        Cursor = Cursors.Arrow;
        // Open the editor immediately after the user finishes dragging.
        DialogResult = true;
    }

    private void UpdateSelectionVisuals(Point current)
    {
        var left = Math.Min(_startPoint.X, current.X);
        var top = Math.Min(_startPoint.Y, current.Y);
        var width = Math.Abs(current.X - _startPoint.X);
        var height = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
        UpdateDimOverlay(_startPoint, current);
    }

    private void UpdateDimOverlay(Point start, Point current)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(
            new Rect(0, 0, SelectionCanvas.ActualWidth, SelectionCanvas.ActualHeight)));
        if (width > 0 && height > 0)
            geometry.Children.Add(new RectangleGeometry(new Rect(left, top, width, height)));

        DimOverlay.Data = geometry;
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && SelectedImage is not null)
        {
            e.Handled = true;
            DialogResult = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
