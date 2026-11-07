using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipShot.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace ClipShot;

public partial class UpdateAvailableWindow : Window
{
    private readonly LatestRelease _release;
    private readonly Version _currentVersion;
    private CancellationTokenSource? _downloadCts;
    private bool _isUpdating;
    private Storyboard? _spinnerStoryboard;

    public UpdateAvailableWindow(LatestRelease release, Version currentVersion)
    {
        InitializeComponent();
        _release = release;
        _currentVersion = currentVersion;

        TitleText.Text = release.Title;
        SubtitleText.Text =
            $"You're on {FormatVersion(currentVersion)}. Version {FormatVersion(release.Version)} is ready.";
        NotesText.Text = release.Notes;
        Closed += (_, _) =>
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            StopSpinner();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isUpdating)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
            return;
        Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
            return;

        _isUpdating = true;
        _downloadCts = new CancellationTokenSource();
        SetBusyState(true, "Downloading…");
        StatusText.Text = "Downloading the update. ClipShot will restart when it is ready.";

        var progress = new Progress<double>(value =>
        {
            if (value < 0)
            {
                DownloadProgress.IsIndeterminate = true;
                BusyLabel.Text = "Downloading…";
                return;
            }

            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = value;
            BusyLabel.Text = value >= 1
                ? "Installing…"
                : $"Downloading… {(int)(value * 100)}%";
        });

        try
        {
            await UpdateInstaller.DownloadAndRestartAsync(_release.ExecutableUrl, progress, _downloadCts.Token);
            BusyLabel.Text = "Restarting…";
            StatusText.Text = "Update ready. ClipShot is restarting.";
            await Task.Delay(400);
            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            ResetBusyState("Update cancelled.");
        }
        catch (Exception ex)
        {
            ResetBusyState("The update could not be installed. You can try again or download it from GitHub.");
            MessageBox.Show(this,
                "ClipShot could not install the update.\n\n" + ex.Message,
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            try
            {
                Process.Start(new ProcessStartInfo(_release.Url.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // Ignore browser launch failures after already showing the error.
            }
        }
    }

    private void SetBusyState(bool busy, string label)
    {
        UpdateButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        IdleContent.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BusyContent.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyLabel.Text = label;
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = true;
        DownloadProgress.Value = 0;

        if (busy)
            StartSpinner();
        else
            StopSpinner();
    }

    private void ResetBusyState(string status)
    {
        _isUpdating = false;
        SetBusyState(false, "Update now");
        StatusText.Text = status;
        _downloadCts?.Dispose();
        _downloadCts = null;
    }

    private void StartSpinner()
    {
        StopSpinner();
        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, BusySpinnerRotate);
        Storyboard.SetTargetProperty(animation, new PropertyPath(RotateTransform.AngleProperty));
        _spinnerStoryboard.Begin();
    }

    private void StopSpinner()
    {
        _spinnerStoryboard?.Stop();
        _spinnerStoryboard = null;
        BusySpinnerRotate.Angle = 0;
    }

    private static string FormatVersion(Version version) =>
        version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
}
