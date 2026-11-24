using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipShot.Services;

internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int RegionHotkeyId = 1;
    private const int FullScreenHotkeyId = 2;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private readonly Action<int> _onHotkey;

    /// <summary>
    /// Hotkeys are delivered to a message-only window. Hiding the main window,
    /// which is what a sign-in launch does, must not drop them.
    /// </summary>
    public GlobalHotkeyManager(Action<int> onHotkey)
    {
        _onHotkey = onHotkey;
        _source = new HwndSource(new HwndSourceParameters("ClipShot.Hotkeys")
        {
            ParentWindow = NativeMethods.HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey)
            return IntPtr.Zero;

        handled = true;
        _onHotkey(wParam.ToInt32());
        return IntPtr.Zero;
    }

    public bool TryRegister(HotkeySettings settings, out string? error)
    {
        UnregisterAll();

        if (string.Equals(settings.RegionKey, settings.FullScreenKey, StringComparison.OrdinalIgnoreCase) &&
            settings.RegionModifiers == settings.FullScreenModifiers)
        {
            error = "The two actions must use different shortcuts.";
            return false;
        }

        var regionRegistered = TryRegisterOne(RegionHotkeyId, settings.RegionKey, settings.RegionModifiers, out var regionError);
        var fullScreenRegistered = TryRegisterOne(FullScreenHotkeyId, settings.FullScreenKey, settings.FullScreenModifiers, out var fullScreenError);

        if (!regionRegistered && !fullScreenRegistered)
        {
            error = $"Region: {regionError} Full screen: {fullScreenError}";
            return false;
        }

        error = regionRegistered && fullScreenRegistered
            ? null
            : $"One shortcut could not be registered. Region: {regionError ?? "active"} Full screen: {fullScreenError ?? "active"}";
        return true;
    }

    private bool TryRegisterOne(int id, string keyName, int modifiers, out string? error)
    {
        if (!Enum.TryParse<Key>(keyName, out var key) || key == Key.None ||
            key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            error = $"“{keyName}” is not a supported shortcut key.";
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var nativeModifiers = (uint)modifiers | ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, nativeModifiers, (uint)virtualKey))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        error = null;
        return true;
    }

    public void UnregisterAll()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, RegionHotkeyId);
        NativeMethods.UnregisterHotKey(_source.Handle, FullScreenHotkeyId);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
