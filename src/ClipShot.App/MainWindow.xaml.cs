using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
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
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            System.Windows.MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveScreen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            var dialog = new SaveFileDialog
            {
                Title = "Save screenshot",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"ClipShot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
            };

            if (dialog.ShowDialog() != true)
                return;

            SavePng(bitmap, dialog.FileName);
            StatusText.Text = $"Saved PNG · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed";
            System.Windows.MessageBox.Show($"ClipShot couldn't save the screenshot.\n\n{ex.Message}", "Save error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
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
                if (selector.WasSaved)
                    StatusText.Text = $"Saved selected region · {region.PixelWidth} × {region.PixelHeight}";
                else
                {
                    System.Windows.Clipboard.SetImage(region);
                    StatusText.Text = $"Copied selected region · {region.PixelWidth} × {region.PixelHeight}";
                }
            }
            else
            {
                StatusText.Text = "Region capture cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Region capture failed";
            System.Windows.MessageBox.Show($"ClipShot couldn't capture that region.\n\n{ex.Message}", "Region capture error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            Show();
            Activate();
        }
    }
}
