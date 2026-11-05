using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MessageBox = System.Windows.MessageBox;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using ClipShot.Services;
using Forms = System.Windows.Forms;

namespace ClipShot;

public partial class MainWindow : Window
{
    private readonly CaptureService _captureService = new();
    private readonly DispatcherTimer _updateCheckTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private static readonly TimeSpan AutoUpdateCheckCooldown = TimeSpan.FromMinutes(2);
    private AppSettings _settings = new();
    private AppSettings _activeSettings = new();
    private GlobalHotkeyManager? _hotkeyManager;
    private HwndSource? _source;
    private Uri? _latestReleaseUri;
    private bool _suppressDimSave;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private bool _trayNoticeShown;
    private DateTime _lastUpdateCheckUtc = DateTime.MinValue;
    private Version? _notifiedUpdateVersion;
    private bool _updateCheckInFlight;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _activeSettings = _settings.Copy();
        UpdateHotkeyLabels();
        _suppressDimSave = true;
        DimOpacitySlider.Value = Math.Round(_settings.RegionDimOpacity * 100);
        DimOpacityValueText.Text = $"{(int)DimOpacitySlider.Value}%";
        _suppressDimSave = false;
        StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
        InitializeTrayIcon();
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        _updateCheckTimer.Start();
        Closed += (_, _) =>
        {
            _updateCheckTimer.Stop();
            _hotkeyManager?.Dispose();
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        };
    }

    private void InitializeTrayIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                   ?? System.Drawing.SystemIcons.Application;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "ClipShot is running",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open ClipShot", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
            return;

        e.Cancel = true;
        Hide();
        StatusText.Text = "ClipShot is running in the system tray";
        if (!_trayNoticeShown && _trayIcon is not null)
        {
            _trayNoticeShown = true;
            _trayIcon.BalloonTipTitle = "ClipShot is still running";
            _trayIcon.BalloonTipText = "ClipShot is in the system tray. Your screenshot shortcuts are still active. Right-click its icon to exit.";
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(4500);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        _ = CheckForUpdatesAsync();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        if (_trayIcon is not null)
            _trayIcon.Visible = false;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual = false)
    {
        if (_updateCheckInFlight)
            return;

        if (!manual && DateTime.UtcNow - _lastUpdateCheckUtc < AutoUpdateCheckCooldown)
            return;

        _updateCheckInFlight = true;
        if (manual)
        {
            CheckForUpdatesButton.IsEnabled = false;
            CheckForUpdatesButton.Content = "Checking…";
            StatusText.Text = "Checking for updates…";
        }

        try
        {
            var release = await UpdateChecker.GetLatestReleaseAsync();
            _lastUpdateCheckUtc = DateTime.UtcNow;
            if (release is null)
            {
                _latestReleaseUri = null;
                UpdateNoticePanel.Visibility = Visibility.Collapsed;
                if (manual)
                    StatusText.Text = "Couldn't find a published ClipShot release to check.";
                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (release.Version > currentVersion)
            {
                var alreadyShowing = UpdateNoticePanel.Visibility == Visibility.Visible &&
                                     _notifiedUpdateVersion == release.Version;
                _latestReleaseUri = release.Url;
                UpdateNoticeTitle.Text = $"ClipShot {FormatVersion(release.Version)} is available";
                UpdateNoticeDetails.Text = $"You're using version {FormatVersion(currentVersion)}. Update now to install the latest version.";
                UpdateNoticePanel.Visibility = Visibility.Visible;
                if (manual)
                    StatusText.Text = $"Update found: ClipShot {FormatVersion(release.Version)} is available.";

                if (!alreadyShowing)
                    NotifyUpdateAvailable(release.Version);
            }
            else
            {
                _latestReleaseUri = release.Url;
                _notifiedUpdateVersion = null;
                UpdateNoticePanel.Visibility = Visibility.Collapsed;
                if (manual)
                    StatusText.Text = $"You're up to date · ClipShot {FormatVersion(currentVersion)} (latest {FormatVersion(release.Version)}).";
            }
        }
        catch (Exception ex)
        {
            if (manual)
            {
                StatusText.Text = "Update check failed. Check your internet connection and try again.";
                MessageBox.Show(this, $"ClipShot couldn't check for updates.\n\n{ex.Message}",
                    "Update check failed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _updateCheckInFlight = false;
            if (manual)
            {
                CheckForUpdatesButton.IsEnabled = true;
                CheckForUpdatesButton.Content = "Check for updates";
            }
        }
    }

    private void NotifyUpdateAvailable(Version version)
    {
        _notifiedUpdateVersion = version;
        if (_trayIcon is null)
            return;

        _trayIcon.BalloonTipTitle = $"ClipShot {FormatVersion(version)} is available";
        _trayIcon.BalloonTipText = IsVisible
            ? "Open ClipShot to update now."
            : "A new version is ready. Open ClipShot from the tray to update.";
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(6000);
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_latestReleaseUri is null)
            return;

        UpdateNowButton.IsEnabled = false;
        UpdateNowButton.Content = "Downloading…";
        UpdateNoticeDetails.Text = "Downloading and preparing the update. ClipShot will restart when it is ready.";
        try
        {
            var release = await UpdateChecker.GetLatestReleaseAsync();
            if (release is null)
                throw new InvalidOperationException("The latest release executable could not be found.");

            await UpdateInstaller.DownloadAndRestartAsync(release.ExecutableUrl);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateNowButton.IsEnabled = true;
            UpdateNowButton.Content = "Update now";
            UpdateNoticeDetails.Text = "The update could not be installed. You can download it from the release page.";
            MessageBox.Show("ClipShot could not install the update.\n\n" + ex.Message, "Update failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Process.Start(new ProcessStartInfo(_latestReleaseUri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || StartWithWindowsCheckBox.IsChecked is not bool enabled)
            return;

        try
        {
            StartupManager.SetEnabled(enabled);
            StatusText.Text = enabled ? "ClipShot will start when you sign in" : "ClipShot will not start automatically";
        }
        catch (Exception ex)
        {
            StartWithWindowsCheckBox.Checked -= StartWithWindows_Changed;
            StartWithWindowsCheckBox.Unchecked -= StartWithWindows_Changed;
            StartWithWindowsCheckBox.IsChecked = !enabled;
            StartWithWindowsCheckBox.Checked += StartWithWindows_Changed;
            StartWithWindowsCheckBox.Unchecked += StartWithWindows_Changed;
            StatusText.Text = "Couldn't update Windows startup setting";
            MessageBox.Show($"ClipShot couldn't change the Windows startup setting.\n\n{ex.Message}", "Startup setting error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        _hotkeyManager = new GlobalHotkeyManager(handle);

        if (!_hotkeyManager.TryRegister(_activeSettings.AsHotkeys(), out var error))
            StatusText.Text = $"Shortcut registration failed: {error}";
        else
            StatusText.Text = error is null ? "Ready" : error;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
            return IntPtr.Zero;

        handled = true;
        switch (wParam.ToInt32())
        {
            case 1:
                CaptureRegion(IsVisible);
                break;
            case 2:
                CaptureScreenFromHotkey();
                break;
        }

        return IntPtr.Zero;
    }

    private static string FormatVersion(Version version) =>
        version.Revision <= 0
            ? $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}"
            : version.ToString();

    private void CaptureScreen_Click(object sender, RoutedEventArgs e) => CaptureScreen();

    private void CaptureScreen()
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            OpenScreenshotEditor(bitmap);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CaptureScreenFromHotkey()
    {
        var restoreWindow = IsVisible;
        if (restoreWindow)
            Hide();

        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            OpenScreenshotEditor(bitmap);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (restoreWindow)
                Show();
        }
    }

    private void OpenScreenshotEditor(BitmapSource bitmap)
    {
        var editor = new ScreenshotEditorWindow(bitmap);
        editor.ShowDialog();
        StatusText.Text = editor.WasCopied
            ? $"Copied screenshot  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}"
            : editor.WasSaved
                ? $"Saved screenshot  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}"
                : "Screenshot editor closed";
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
            StatusText.Text = $"Saved PNG  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed";
            MessageBox.Show($"ClipShot couldn't save the screenshot.\n\n{ex.Message}", "Save error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e) => CaptureRegion(IsVisible);

    private void CaptureRegion(bool restoreWindow)
    {
        if (restoreWindow)
            Hide();

        try
        {
            var screenshot = _captureService.CapturePrimaryScreen();
            var selector = new RegionSelectionWindow(screenshot, _activeSettings.RegionDimOpacity);
            var selected = selector.ShowDialog();
            if (selected == true && selector.SelectedImage is { } region)
                OpenScreenshotEditor(region);
            else
                StatusText.Text = "Region capture cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Region capture failed";
            MessageBox.Show($"ClipShot couldn't capture that region.\n\n{ex.Message}", "Region capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (restoreWindow)
                Show();
        }
    }

    private void RegionHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CaptureHotkey(e, RegionHotkeyBox, out var key, out var modifiers))
            return;

        _settings.RegionKey = key.ToString();
        _settings.RegionModifiers = (int)modifiers;
        CommitHotkeys();
    }

    private void FullScreenHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CaptureHotkey(e, FullScreenHotkeyBox, out var key, out var modifiers))
            return;

        _settings.FullScreenKey = key.ToString();
        _settings.FullScreenModifiers = (int)modifiers;
        CommitHotkeys();
    }

    private static bool CaptureHotkey(KeyEventArgs e, System.Windows.Controls.TextBox box, out Key key, out ModifierKeys modifiers)
    {
        key = e.Key == Key.System ? e.SystemKey : e.Key;
        modifiers = Keyboard.Modifiers;

        if (key is Key.Tab or Key.Escape)
            return false;

        e.Handled = true;

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;

        box.Text = FormatHotkey(key, modifiers);
        return true;
    }

    private static string FormatHotkey(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatKey(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + ((int)key - (int)Key.D0))).ToString();

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num " + ((int)key - (int)Key.NumPad0);

        return key switch
        {
            Key.PrintScreen => "PrtScn",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Capital => "Caps Lock",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.Space => "Space",
            _ => key.ToString()
        };
    }

    private void UpdateHotkeyLabels()
    {
        var region = FormatHotkey(Enum.Parse<Key>(_settings.RegionKey), (ModifierKeys)_settings.RegionModifiers);
        var screen = FormatHotkey(Enum.Parse<Key>(_settings.FullScreenKey), (ModifierKeys)_settings.FullScreenModifiers);
        if (RegionHotkeyBox is not null)
            RegionHotkeyBox.Text = region;
        if (FullScreenHotkeyBox is not null)
            FullScreenHotkeyBox.Text = screen;
        if (RegionHotkeyHint is not null)
            RegionHotkeyHint.Text = region;
        if (ScreenHotkeyHint is not null)
            ScreenHotkeyHint.Text = screen;
    }

    private void DimOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || DimOpacityValueText is null)
            return;

        DimOpacityValueText.Text = $"{(int)e.NewValue}%";
        if (_suppressDimSave)
            return;

        _settings.RegionDimOpacity = e.NewValue / 100d;
        _activeSettings.RegionDimOpacity = _settings.RegionDimOpacity;
        try
        {
            _settings.Save();
            StatusText.Text = $"Region overlay set to {(int)e.NewValue}%";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't save overlay setting";
            MessageBox.Show(ex.Message, "Settings error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender == RegionHotkeyBox)
            RegionHotkeyCaption.Text = "Press a combination";
        else
            ScreenHotkeyCaption.Text = "Press a combination";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender == RegionHotkeyBox)
            RegionHotkeyCaption.Text = "Region";
        else
            ScreenHotkeyCaption.Text = "Full screen";
    }

    private void CommitHotkeys()
    {
        var candidate = _settings.Copy();
        if (_hotkeyManager is null)
        {
            StatusText.Text = "Shortcut unavailable: window is not ready";
            _settings = _activeSettings.Copy();
            UpdateHotkeyLabels();
            return;
        }

        if (!_hotkeyManager.TryRegister(candidate.AsHotkeys(), out var error))
        {
            StatusText.Text = $"Shortcut unavailable: {error ?? "registration failed"}";
            _hotkeyManager.TryRegister(_activeSettings.AsHotkeys(), out _);
            _settings = _activeSettings.Copy();
            UpdateHotkeyLabels();
            return;
        }

        try
        {
            candidate.Save();
            _settings = candidate;
            _activeSettings = candidate.Copy();
            StatusText.Text = error is null ? "Shortcut saved" : $"Shortcut saved  ·  {error}";
        }
        catch (Exception ex)
        {
            _hotkeyManager.TryRegister(_activeSettings.AsHotkeys(), out _);
            _settings = _activeSettings.Copy();
            UpdateHotkeyLabels();
            StatusText.Text = "Couldn't save shortcuts";
            MessageBox.Show(ex.Message, "Shortcut settings error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var dim = _settings.RegionDimOpacity;
        _settings = new AppSettings { RegionDimOpacity = dim };
        UpdateHotkeyLabels();
        CommitHotkeys();
    }
}
