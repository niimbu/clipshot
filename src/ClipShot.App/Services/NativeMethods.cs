using System.Runtime.InteropServices;

namespace ClipShot.Services;

internal static class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal static readonly IntPtr HwndMessage = new(-3);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int SwRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegisterWindowMessage(string message);

    /// <summary>
    /// The notification area exists only after Explorer has created the desktop.
    /// Sign-in can launch ClipShot before that.
    /// </summary>
    internal static bool IsShellReady() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>
    /// Bring an already-running ClipShot window to the front, including one that
    /// was left hidden in the tray.
    /// </summary>
    internal static bool TryActivateMainWindow()
    {
        var handle = FindWindow(null, "ClipShot");
        if (handle == IntPtr.Zero)
            return false;

        ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
        return true;
    }
}
