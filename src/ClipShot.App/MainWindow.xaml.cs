using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using ClipShot.Services;

namespace ClipShot;

public partial class MainWindow : Window
{
    private readonly CaptureService _captureService = new();
    private HotkeySettings _hotkeySettings = new();
    private GlobalHotkeyManager? _hotkeyManager;
    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();
        _hotkeySettings = HotkeySettings.Load();
        UpdateHotkeyLabels();
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += (_, _) => _hotkeyManager?.Dispose();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        _hotkeyManager = new GlobalHotkeyManager(handle);

        if (!_hotkeyManager.TryRegister(_hotkeySettings, out var error))
            StatusText.Text = $"Shortcut registration failed: {error}";
        else
            StatusText.Text = "Ready · Global shortcuts active";
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

    private void CaptureScreen_Click(object sender, RoutedEventArgs e) => CaptureScreen();

    private void CaptureScreen()
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

    private void CaptureScreenFromHotkey()
    {
        var restoreWindow = IsVisible;
        if (restoreWindow)
            Hide();

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
        finally
        {
            if (restoreWindow)
                Show();
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

    private void CaptureRegion_Click(object sender, RoutedEventArgs e) => CaptureRegion(IsVisible);

    private void CaptureRegion(bool restoreWindow)
    {
        if (restoreWindow)
            Hide();

        try
        {
            var screenshot = _captureService.CapturePrimaryScreen();
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
            if (restoreWindow)
                Show();
        }
    }

    private void RegionHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureHotkey(e, RegionHotkeyBox);
        _hotkeySettings.RegionKey = ((KeyEventArgs)e).Key.ToString();
        _hotkeySettings.RegionModifiers = (int)Keyboard.Modifiers;
    }

    private void FullScreenHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureHotkey(e, FullScreenHotkeyBox);
        _hotkeySettings.FullScreenKey = e.Key.ToString();
        _hotkeySettings.FullScreenModifiers = (int)Keyboard.Modifiers;
    }

    private static void CaptureHotkey(KeyEventArgs e, System.Windows.Controls.TextBox box)
    {
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var modifiers = Keyboard.Modifiers;
        box.Text = FormatHotkey(e.Key, modifiers);
    }

    private static string FormatHotkey(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Snapshot ? "Print Screen" : key.ToString());
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
        if (_hotkeyManager is null || !_hotkeyManager.TryRegister(candidate, out var error))
        {
            StatusText.Text = $"Shortcut unavailable: {error ?? "window is not ready"}";
            _hotkeyManager?.TryRegister(_hotkeySettings, out _);
            return;
        }

        try
        {
            candidate.Save();
            _hotkeySettings = candidate;
            StatusText.Text = "Keyboard shortcuts saved";
        }
        catch (Exception ex)
        {
            _hotkeyManager.TryRegister(_hotkeySettings, out _);
            StatusText.Text = "Couldn't save shortcuts";
            System.Windows.MessageBox.Show(ex.Message, "Shortcut settings error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _hotkeySettings = new HotkeySettings();
        UpdateHotkeyLabels();
        SaveHotkeys_Click(sender, e);
    }
}
