using System.Runtime.InteropServices;
using System.Text;

namespace ClipShot.Services;

/// <summary>
/// Detects whether ClipShot is running inside an MSIX / Store package.
/// </summary>
internal static class PackageEnvironment
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        var length = 0;
        var hr = GetCurrentPackageFullName(ref length, null);
        return hr != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
