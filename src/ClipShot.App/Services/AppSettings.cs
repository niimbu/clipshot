using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace ClipShot.Services;

/// <summary>
/// Persisted ClipShot preferences (shortcuts + capture overlay).
/// </summary>
internal sealed class AppSettings
{
    public string RegionKey { get; set; } = "PrintScreen";
    public int RegionModifiers { get; set; } = 0;
    public string FullScreenKey { get; set; } = "D1";
    public int FullScreenModifiers { get; set; } = 6;

    /// <summary>
    /// Darkness of the region-selection dim, from 0 (clear) to 0.9 (very dark).
    /// </summary>
    public double RegionDimOpacity { get; set; } = 0.45;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipShot",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                           ?? new AppSettings();

            if (!Enum.TryParse<Key>(settings.RegionKey, out var regionKey) || regionKey == Key.None ||
                !Enum.TryParse<Key>(settings.FullScreenKey, out var fullScreenKey) || fullScreenKey == Key.None)
                return new AppSettings();

            settings.RegionKey = regionKey.ToString();
            settings.FullScreenKey = fullScreenKey.ToString();
            settings.RegionModifiers &= 0x0F;
            settings.FullScreenModifiers &= 0x0F;
            settings.RegionDimOpacity = Math.Clamp(settings.RegionDimOpacity, 0, 0.9);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        RegionDimOpacity = Math.Clamp(RegionDimOpacity, 0, 0.9);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public AppSettings Copy() => new()
    {
        RegionKey = RegionKey,
        RegionModifiers = RegionModifiers,
        FullScreenKey = FullScreenKey,
        FullScreenModifiers = FullScreenModifiers,
        RegionDimOpacity = RegionDimOpacity
    };

    public HotkeySettings AsHotkeys() => new()
    {
        RegionKey = RegionKey,
        RegionModifiers = RegionModifiers,
        FullScreenKey = FullScreenKey,
        FullScreenModifiers = FullScreenModifiers
    };
}
