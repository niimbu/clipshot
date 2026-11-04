using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using ClipShot.Services;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace ClipShot;

public partial class MainWindow : Window
{
    private readonly CaptureService _captureService = new();
    private readonly DispatcherTimer _updateCheckTimer = new() { Interval = TimeSpan.FromHours(6) };
    private HotkeySettings _hotkeySettings = new();
    private HotkeySettings _activeHotkeySettings = new();
    private GlobalHotkeyManager? _hotkeyManager;
    private HwndSource? _source;
    private Uri? _latestReleaseUri;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private bool _trayNoticeShown;

    public MainWindow()
    {
        InitializeComponent();
        _hotkeySettings = HotkeySettings.Load();
        _activeHotkeySettings = _hotkeySettings.Copy();
        UpdateHotkeyLabels();
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
            if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
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
    }

    private void ExitApplication()
    {
        _allowExit = true;
        if (_trayIcon is not null)
            _trayIcon.Visible = false;
        Close();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await UpdateChecker.GetLatestReleaseAsync();
            if (release is null)
                return;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (release.Version > currentVersion)
            {
                _latestReleaseUri = release.Url;
                UpdateNoticeTitle.Text = $"ClipShot {release.Version} is available";
                UpdateNoticeDetails.Text = $"You're using version {currentVersion}. Update now to install the latest version.";
                UpdateNoticePanel.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateNoticePanel.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // Update checks are best-effort and must never interrupt capturing.
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_latestReleaseUri is null) return;
        UpdateNowButton.IsEnabled = false;
        UpdateNowButton.Content = "Downloading…";
        UpdateNoticeDetails.Text = "Downloading and preparing the update. ClipShot will restart when it is ready.";
        try
        {
            var release = await UpdateChecker.GetLatestReleaseAsync();
            if (release is null) throw new InvalidOperationException("The latest release executable could not be found.");
            await UpdateInstaller.DownloadAndRestartAsync(release.ExecutableUrl);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateNowButton.IsEnabled = true;
            UpdateNowButton.Content = "Update now";
            UpdateNoticeDetails.Text = "The update could not be installed. You can download it from the release page.";
            MessageBox.Show("ClipShot could not install the update.\n\n" + ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (!_hotkeyManager.TryRegister(_activeHotkeySettings, out var error))
            StatusText.Text = $"Shortcut registration failed: {error}";
        else
            StatusText.Text = error is null ? "Ready · Global shortcuts active" : error;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
            return IntPtr.Zero;

        handled = true;
        switch (wParam.ToInt32())
        {
            case 1: CaptureRegion(IsVisible); break;
            case 2: CaptureScreenFromHotkey(); break;
        }
        return IntPtr.Zero;
    }

    private void CaptureScreen_Click(object sender, RoutedEventArgs e) => CaptureScreen();

    private void CaptureScreen()
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            CopyImageToClipboard(bitmap);
            StatusText.Text = $"Copied full screen · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
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
        if (restoreWindow) Hide();
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            CopyImageToClipboard(bitmap);
            StatusText.Text = $"Copied full screen · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (restoreWindow) Show();
        }
    }

    private static void CopyImageToClipboard(BitmapSource bitmap)
    {
        System.Windows.Clipboard.SetImage(bitmap);
        System.Windows.Clipboard.Flush();
        if (!System.Windows.Clipboard.ContainsImage())
            throw new InvalidOperationException("Windows did not confirm that the screenshot reached the clipboard. Please try copying again.");
    }

    private void SaveScreen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            var dialog = new SaveFileDialog
            {
                Title = "Save screenshot", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png",
                AddExtension = true, FileName = $"ClipShot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
            };
            if (dialog.ShowDialog() != true) return;
            SavePng(bitmap, dialog.FileName);
            StatusText.Text = $"Saved PNG · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
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
        if (restoreWindow) Hide();
        try
        {
            var screenshot = _captureService.CapturePrimaryScreen();
            var selector = new RegionSelectionWindow(screenshot);
            var selected = selector.ShowDialog();
            if (selected == true && selector.SelectedImage is { } region)
            {
                var editor = new ScreenshotEditorWindow(region);
                editor.ShowDialog();
                StatusText.Text = editor.WasCopied
                    ? $"Copied edited capture · {region.PixelWidth} × {region.PixelHeight}"
                    : editor.WasSaved
                        ? $"Saved capture · {region.PixelWidth} × {region.PixelHeight}"
                        : "Capture editor closed";
            }
            else StatusText.Text = "Region capture cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Region capture failed";
            MessageBox.Show($"ClipShot couldn't capture that region.\n\n{ex.Message}", "Region capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (restoreWindow) Show();
        }
    }

    private void RegionHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CaptureHotkey(e, RegionHotkeyBox, out var key, out var modifiers)) return;
        _hotkeySettings.RegionKey = key.ToString();
        _hotkeySettings.RegionModifiers = (int)modifiers;
    }

    private void FullScreenHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CaptureHotkey(e, FullScreenHotkeyBox, out var key, out var modifiers)) return;
        _hotkeySettings.FullScreenKey = key.ToString();
        _hotkeySettings.FullScreenModifiers = (int)modifiers;
    }

    private static bool CaptureHotkey(KeyEventArgs e, System.Windows.Controls.TextBox box, out Key key, out ModifierKeys modifiers)
    {
        e.Handled = true;
        key = e.Key == Key.System ? e.SystemKey : e.Key;
        modifiers = Keyboard.Modifiers;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return false;
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
        parts.Add(key == Key.PrintScreen ? "Print Screen" : key.ToString());
        return string.Join(" + ", parts);
    }

    private void UpdateHotkeyLabels()
    {
        if (RegionHotkeyBox is not null)
            RegionHotkeyBox.Text = FormatHotkey(Enum.Parse<Key>(_hotkeySettings.RegionKey), (ModifierKeys)_hotkeySettings.RegionModifiers);
        if (FullScreenHotkeyBox is not null)
            FullScreenHotkeyBox.Text = FormatHotkey(Enum.Parse<Key>(_hotkeySettings.FullScreenKey), (ModifierKeys)_hotkeySettings.FullScreenModifiers);
    }

    private void SaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var candidate = _hotkeySettings.Copy();
        string? error = null;
        if (_hotkeyManager is null || !_hotkeyManager.TryRegister(candidate, out error))
        {
            StatusText.Text = $"Shortcut unavailable: {error ?? "window is not ready"}";
            _hotkeyManager?.TryRegister(_activeHotkeySettings, out _);
            return;
        }
        try
        {
            candidate.Save();
            _hotkeySettings = candidate;
            _activeHotkeySettings = candidate.Copy();
            StatusText.Text = error is null ? "Keyboard shortcuts saved" : $"Shortcuts saved with warning: {error}";
        }
        catch (Exception ex)
        {
            _hotkeyManager.TryRegister(_activeHotkeySettings, out _);
            StatusText.Text = "Couldn't save shortcuts";
            MessageBox.Show(ex.Message, "Shortcut settings error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _hotkeySettings = new HotkeySettings();
        UpdateHotkeyLabels();
        SaveHotkeys_Click(sender, e);
    }
}