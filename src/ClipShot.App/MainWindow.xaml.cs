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
    private HotkeySettings _activeHotkeySettings = new();
    private GlobalHotkeyManager? _hotkeyManager;
    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();
        _hotkeySettings = HotkeySettings.Load();
        _activeHotkeySettings = _hotkeySettings.Copy();
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

        if (!_hotkeyManager.TryRegister(_activeHotkeySettings, out var error))
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

    private void CaptureScreen_Click(object sender, RoutedEventArgs e) => CaptureScreen();

    private void CaptureScreen()
    {
        try
        {
            var bitmap = _captureService.CapturePrimaryScreen();
            System.Windows.Clipboard.SetImage(bitmap);
            StatusText.Text = $"Copied full screen  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}";
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
            StatusText.Text = $"Copied full screen  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}";
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
            StatusText.Text = $"Saved PNG  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight}";
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
                    StatusText.Text = $"Saved region  ·  {region.PixelWidth} × {region.PixelHeight}";
                else
                {
                    System.Windows.Clipboard.SetImage(region);
                    StatusText.Text = $"Copied region  ·  {region.PixelWidth} × {region.PixelHeight}";
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
        if (!CaptureHotkey(e, RegionHotkeyBox, out var key, out var modifiers))
            return;

        _hotkeySettings.RegionKey = key.ToString();
        _hotkeySettings.RegionModifiers = (int)modifiers;
        CommitHotkeys();
    }

    private void FullScreenHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CaptureHotkey(e, FullScreenHotkeyBox, out var key, out var modifiers))
            return;

        _hotkeySettings.FullScreenKey = key.ToString();
        _hotkeySettings.FullScreenModifiers = (int)modifiers;
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
        var region = FormatHotkey(Enum.Parse<Key>(_hotkeySettings.RegionKey), (ModifierKeys)_hotkeySettings.RegionModifiers);
        var screen = FormatHotkey(Enum.Parse<Key>(_hotkeySettings.FullScreenKey), (ModifierKeys)_hotkeySettings.FullScreenModifiers);
        if (RegionHotkeyBox is not null)
            RegionHotkeyBox.Text = region;
        if (FullScreenHotkeyBox is not null)
            FullScreenHotkeyBox.Text = screen;
        if (RegionHotkeyHint is not null)
            RegionHotkeyHint.Text = region;
        if (ScreenHotkeyHint is not null)
            ScreenHotkeyHint.Text = screen;
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
        var candidate = _hotkeySettings.Copy();
        if (_hotkeyManager is null)
        {
            StatusText.Text = "Shortcut unavailable: window is not ready";
            _hotkeySettings = _activeHotkeySettings.Copy();
            UpdateHotkeyLabels();
            return;
        }

        if (!_hotkeyManager.TryRegister(candidate, out var error))
        {
            StatusText.Text = $"Shortcut unavailable: {error ?? "registration failed"}";
            _hotkeyManager.TryRegister(_activeHotkeySettings, out _);
            _hotkeySettings = _activeHotkeySettings.Copy();
            UpdateHotkeyLabels();
            return;
        }

        try
        {
            candidate.Save();
            _hotkeySettings = candidate;
            _activeHotkeySettings = candidate.Copy();
            StatusText.Text = error is null ? "Shortcut saved" : $"Shortcut saved  ·  {error}";
        }
        catch (Exception ex)
        {
            _hotkeyManager.TryRegister(_activeHotkeySettings, out _);
            _hotkeySettings = _activeHotkeySettings.Copy();
            UpdateHotkeyLabels();
            StatusText.Text = "Couldn't save shortcuts";
            System.Windows.MessageBox.Show(ex.Message, "Shortcut settings error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _hotkeySettings = new HotkeySettings();
        UpdateHotkeyLabels();
        CommitHotkeys();
    }
}
