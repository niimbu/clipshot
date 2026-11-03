using System.Windows;
using ClipShot.Services;

namespace ClipShot;

public partial class MainWindow : Window
{
    private readonly CaptureService _captureService = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void CaptureScreen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            System.Windows.Clipboard.SetImage(bitmap);
            StatusText.Text = $"Copied full screen · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
            System.Windows.MessageBox.Show("Your screenshot has been copied to the clipboard.", "ClipShot",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            System.Windows.MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var screenshot = _captureService.CapturePrimaryScreen();
            Hide();
            var selector = new RegionSelectionWindow(screenshot);
            var selected = selector.ShowDialog();
            if (selected == true && selector.SelectedImage is { } region)
            {
                System.Windows.Clipboard.SetImage(region);
                StatusText.Text = $"Copied selected region · {region.PixelWidth} × {region.PixelHeight}";
            }
            else
            {
                StatusText.Text = "Region capture cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Region capture failed";
            System.Windows.MessageBox.Show($"ClipShot couldn't capture that region.\n\n{ex.Message}", "Capture error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            Show();
            Activate();
        }
    }
}
