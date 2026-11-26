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
    private LatestRelease? _latestRelease;
    private bool _suppressDimSave;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private bool _trayNoticeShown;
    private DateTime _lastUpdateCheckUtc = DateTime.MinValue;
    private Version? _notifiedUpdateVersion;
    private bool _updateCheckInFlight;
    private bool _updateDialogOpen;
    private TrayBalloonKind _activeBalloon = TrayBalloonKind.None;
    private Forms.ToolStripMenuItem? _trayUpdateMenuItem;
    private DispatcherTimer? _shellWait;
    private DispatcherTimer? _hotkeyRetry;
    private int _hotkeyAttempts;
    private bool _openedFromTray;
    private readonly int _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private enum TrayBalloonKind
    {
        None,
        StillRunning,
        UpdateAvailable
    }

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
        // Upgrade legacy Run-key entries so login launches with --autostart (tray only).
        try { StartupManager.EnsureCurrentCommand(); }
        catch { /* keep going if the registry is temporarily unavailable */ }
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        _updateCheckTimer.Start();
        Closed += (_, _) =>
        {
            _updateCheckTimer.Stop();
            _shellWait?.Stop();
            _hotkeyRetry?.Stop();
            _hotkeyManager?.Dispose();
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        };
    }

    /// <summary>
    /// Start menu and normal launches show the window right away.
    /// </summary>
    internal void StartNormally()
    {
        StartupLog.Write("StartNormally");
        FinishStartup(showWindow: true);
    }

    /// <summary>
    /// Sign-in starts this process before the desktop exists. Shortcuts and the
    /// tray icon do nothing until Explorer is up, so wait for it, then stay hidden.
    /// </summary>
    internal void StartWhenShellIsReady()
    {
        StartupLog.Write("StartWhenShellIsReady");
        if (NativeMethods.IsShellReady())
        {
            FinishStartup(showWindow: false);
            return;
        }

        _shellWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        var attempts = 0;
        _shellWait.Tick += (_, _) =>
        {
            attempts++;
            if (!NativeMethods.IsShellReady() && attempts < 75)
                return;

            _shellWait.Stop();
            FinishStartup(showWindow: false);
        };
        _shellWait.Start();
    }

    private void FinishStartup(bool showWindow)
    {
        StartupLog.Write(showWindow ? "FinishStartup show" : "FinishStartup tray");
        if (!showWindow)
        {
            // Set this before the window handle exists. Changing it later makes WPF show the window.
            ShowActivated = false;
            ShowInTaskbar = false;
            Visibility = Visibility.Hidden;
            _trayNoticeShown = true;
            StatusText.Text = "ClipShot is running in the system tray";
        }
        else
        {
            Show();
        }

        try
        {
            EnsureMessageHook();
            InitializeTrayIcon();
            EnsureHotkeys();
            StartupLog.Write("Tray and shortcuts ready");
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("FinishStartup", ex);
            StatusText.Text = "ClipShot opened, but the tray or shortcuts need attention";
            MessageBox.Show(
                $"ClipShot opened, but something blocked the tray icon or shortcuts.\n\n{ex.Message}",
                "ClipShot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _ = CheckForUpdatesAsync();
    }

    private void EnsureMessageHook()
    {
        if (_source is not null)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            handle = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
    }

    private void EnsureHotkeys()
    {
        EnsureMessageHook();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            handle = new WindowInteropHelper(this).EnsureHandle();

        _hotkeyManager?.Dispose();
        _hotkeyManager = new GlobalHotkeyManager(handle);
        if (_hotkeyManager.TryRegister(_activeSettings.AsHotkeys(), out var error) && error is null)
        {
            _hotkeyRetry?.Stop();
            _hotkeyRetry = null;
            if (StatusText.Text.StartsWith("Shortcut registration failed", StringComparison.Ordinal))
                StatusText.Text = IsVisible ? "Ready" : "ClipShot is running in the system tray";
            return;
        }

        StatusText.Text = $"Shortcut registration failed: {error}";
        if (_hotkeyAttempts >= 40)
        {
            _hotkeyRetry?.Stop();
            return;
        }

        _hotkeyAttempts++;
        _hotkeyRetry ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hotkeyRetry.Tick -= HotkeyRetryTick;
        _hotkeyRetry.Tick += HotkeyRetryTick;
        _hotkeyRetry.Start();
    }

    private void HotkeyRetryTick(object? sender, EventArgs e) => EnsureHotkeys();

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
        _trayUpdateMenuItem = new Forms.ToolStripMenuItem("View update")
        {
            Visible = false
        };
        _trayUpdateMenuItem.Click += (_, _) => Dispatcher.BeginInvoke(ShowUpdateDialogFromTray);
        menu.Items.Add(_trayUpdateMenuItem);
        menu.Items.Add("Open ClipShot", null, (_, _) => Dispatcher.BeginInvoke(RestoreFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
        // One left click should open the window. Right click still shows the menu.
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(RestoreFromTray);
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(OnTrayBalloonTipClicked);
    }

    private void OnTrayBalloonTipClicked()
    {
        var kind = _activeBalloon;
        _activeBalloon = TrayBalloonKind.None;

        if (kind == TrayBalloonKind.UpdateAvailable)
        {
            ShowUpdateDialogFromTray();
            return;
        }

        if (kind == TrayBalloonKind.StillRunning)
            RestoreFromTray();
    }

    private void ShowUpdateDialogFromTray()
    {
        RestoreFromTray();
        ShowUpdateDialog();
    }

    private void SetTrayUpdateMenuVisible(bool visible, Version? version = null)
    {
        if (_trayUpdateMenuItem is null)
            return;

        _trayUpdateMenuItem.Visible = visible;
        if (visible && version is not null)
            _trayUpdateMenuItem.Text = $"View update · {FormatVersion(version)}";
        else
            _trayUpdateMenuItem.Text = "View update";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
            return;

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
        StatusText.Text = "ClipShot is running in the system tray";
        if (!_trayNoticeShown && _trayIcon is not null)
        {
            _trayNoticeShown = true;
            _activeBalloon = TrayBalloonKind.StillRunning;
            _trayIcon.BalloonTipTitle = "ClipShot is still running";
            _trayIcon.BalloonTipText = "ClipShot is in the system tray. Your screenshot shortcuts are still active. Click to open, or right-click to exit.";
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(4500);
        }
    }

    internal void ListenForShowRequests(EventWaitHandle request)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (request.WaitOne())
                    Dispatcher.BeginInvoke(RestoreFromTray);
            }
            catch (ObjectDisposedException)
            {
                // The event is released when the process exits.
            }
        })
        {
            IsBackground = true,
            Name = "ClipShot.Show"
        };
        thread.Start();
    }

    private void RestoreFromTray()
    {
        _openedFromTray = true;
        _hotkeyAttempts = 0;
        EnsureMessageHook();
        EnsureHotkeys();
        ShowInTaskbar = true;
        ShowActivated = true;
        Visibility = Visibility.Visible;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // The first show after a sign-in launch is the user opening the tray.
        // Hide only the automatic one, or that click would close the window again.
        if (App.StartMinimizedToTray && !_openedFromTray)
        {
            ShowInTaskbar = false;
            Hide();
            StatusText.Text = "ClipShot is running in the system tray";
        }

        await CheckForUpdatesAsync();
    }

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
                _latestRelease = null;
                UpdateNoticePanel.Visibility = Visibility.Collapsed;
                SetTrayUpdateMenuVisible(false);
                if (manual)
                    StatusText.Text = "Couldn't find a published ClipShot release to check.";
                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (release.Version > currentVersion)
            {
                var alreadyShowing = UpdateNoticePanel.Visibility == Visibility.Visible &&
                                     _notifiedUpdateVersion == release.Version;
                _latestRelease = release;
                UpdateNoticeTitle.Text = $"ClipShot {FormatVersion(release.Version)} is available";
                UpdateNoticeDetails.Text = $"You're using version {FormatVersion(currentVersion)}. See what's new, then update when you're ready.";
                UpdateNoticePanel.Visibility = Visibility.Visible;
                if (manual)
                    StatusText.Text = $"Update found: ClipShot {FormatVersion(release.Version)} is available.";

                SetTrayUpdateMenuVisible(true, release.Version);
                if (!alreadyShowing)
                    NotifyUpdateAvailable(release.Version);
            }
            else
            {
                _latestRelease = null;
                _notifiedUpdateVersion = null;
                UpdateNoticePanel.Visibility = Visibility.Collapsed;
                SetTrayUpdateMenuVisible(false);
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

        _activeBalloon = TrayBalloonKind.UpdateAvailable;
        _trayIcon.BalloonTipTitle = $"ClipShot {FormatVersion(version)} is available";
        _trayIcon.BalloonTipText = "Click to see what's new and update.";
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(8000);
    }

    private void UpdateNow_Click(object sender, RoutedEventArgs e) => ShowUpdateDialog();

    private void ShowUpdateDialog()
    {
        if (_latestRelease is null || _updateDialogOpen)
            return;

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var owned = IsVisible;
        var dialog = new UpdateAvailableWindow(_latestRelease, currentVersion)
        {
            Owner = owned ? this : null,
            ShowInTaskbar = !owned,
            WindowStartupLocation = owned ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };

        _updateDialogOpen = true;
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            _updateDialogOpen = false;
        }
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || StartWithWindowsCheckBox.IsChecked is not bool enabled)
            return;

        try
        {
            StartupManager.SetEnabled(enabled);
            StatusText.Text = enabled
                ? "ClipShot will start in the tray when you sign in"
                : "ClipShot will not start automatically";
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

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey)
        {
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

        if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            handled = true;
            RecreateTrayIcon();
            _hotkeyAttempts = 0;
            EnsureHotkeys();
        }

        return IntPtr.Zero;
    }

    private void RecreateTrayIcon()
    {
        if (_trayIcon is null)
            return;

        var showUpdate = _trayUpdateMenuItem?.Visible == true;
        var version = _latestRelease?.Version;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
        InitializeTrayIcon();
        if (showUpdate)
            SetTrayUpdateMenuVisible(true, version);
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
            var desktop = _captureService.CaptureDesktop();
            OpenScreenshotEditor(desktop.Image, desktop.Bounds);
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

        var keepPreviousAppFocused = false;
        try
        {
            var desktop = _captureService.CaptureDesktop();
            keepPreviousAppFocused = OpenScreenshotEditor(desktop.Image, desktop.Bounds);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Capture failed";
            MessageBox.Show($"ClipShot couldn't capture the screen.\n\n{ex.Message}", "Capture error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // After Space/copy (or save), stay in the tray so the app that was focused
            // when the screenshot was taken keeps focus for pasting.
            if (restoreWindow && !keepPreviousAppFocused)
                Show();
        }
    }

    private bool OpenScreenshotEditor(BitmapSource bitmap, System.Drawing.Rectangle? cover = null) =>
        ShowEditor(new ScreenshotEditorWindow(bitmap, cover), bitmap);

    /// <returns>True when the user copied or saved — callers should not steal focus back.</returns>
    private bool OpenScreenshotEditor(BitmapSource bitmap, BitmapSource fullScreenshot, NormalizedRect fractions, double dimOpacity, System.Drawing.Rectangle cover) =>
        ShowEditor(new ScreenshotEditorWindow(bitmap, fullScreenshot, fractions, dimOpacity, cover), bitmap);

    private bool ShowEditor(ScreenshotEditorWindow editor, BitmapSource bitmap)
    {
        editor.ShowDialog();
        StatusText.Text = editor.WasCopied
            ? $"Copied screenshot  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}"
            : editor.WasSaved
                ? $"Saved screenshot  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}"
                : "Screenshot editor closed";
        return editor.WasCopied || editor.WasSaved;
    }

    private void SaveScreen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitmap = _captureService.CaptureDesktop().Image;
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

        var keepPreviousAppFocused = false;
        try
        {
            var desktop = _captureService.CaptureDesktop();
            var session = new RegionCaptureSession(desktop, _activeSettings.RegionDimOpacity);
            if (session.TrySelect(out var pick) && pick is not null)
            {
                keepPreviousAppFocused = pick.InPlace && pick.Background is not null
                    ? OpenScreenshotEditor(pick.Image, pick.Background, pick.Fractions, _activeSettings.RegionDimOpacity, pick.Cover)
                    : OpenScreenshotEditor(pick.Image, pick.Cover);
            }
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
            // After Space/copy (or save), stay in the tray so the app that was focused
            // when the screenshot was taken keeps focus for pasting.
            if (restoreWindow && !keepPreviousAppFocused)
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
        if (GuideRegionHotkey is not null)
            GuideRegionHotkey.Text = region;
        if (GuideScreenHotkey is not null)
            GuideScreenHotkey.Text = screen;
    }

    private void StarOnGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/niimbu/clipshot")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't open GitHub";
            MessageBox.Show($"ClipShot couldn't open the GitHub page.\n\n{ex.Message}",
                "GitHub",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
