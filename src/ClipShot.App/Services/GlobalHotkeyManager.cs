using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ClipShot.Services;

internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int RegionHotkeyId = 1;
    private const int FullScreenHotkeyId = 2;
    private const uint ModNoRepeat = 0x4000;
    private readonly IntPtr _windowHandle;

    public GlobalHotkeyManager(IntPtr windowHandle) => _windowHandle = windowHandle;

    public bool TryRegister(HotkeySettings settings, out string? error)
    {
        UnregisterAll();

        if (string.Equals(settings.RegionKey, settings.FullScreenKey, StringComparison.OrdinalIgnoreCase) &&
            settings.RegionModifiers == settings.FullScreenModifiers)
        {
            error = "The two actions must use different shortcuts.";
            return false;
        }

        if (!TryRegisterOne(RegionHotkeyId, settings.RegionKey, settings.RegionModifiers, out error))
            return false;

        if (!TryRegisterOne(FullScreenHotkeyId, settings.FullScreenKey, settings.FullScreenModifiers, out error))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, RegionHotkeyId);
            return false;
        }

        error = null;
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
        if (!NativeMethods.RegisterHotKey(_windowHandle, id, nativeModifiers, (uint)virtualKey))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        error = null;
        return true;
    }

    public void UnregisterAll()
    {
        NativeMethods.UnregisterHotKey(_windowHandle, RegionHotkeyId);
        NativeMethods.UnregisterHotKey(_windowHandle, FullScreenHotkeyId);
    }

    public void Dispose() => UnregisterAll();
}
