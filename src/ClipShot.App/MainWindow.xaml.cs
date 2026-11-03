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
            Clipboard.SetImage(bitmap);
            StatusText.Text = $"Copied full screen · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
            MessageBox.Show("Your screenshot has been copied to the clipboard.", "ClipShot",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Region selection is the next feature in the MVP roadmap. For now, use Capture screen.",
            "Coming next", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
