using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace ClipShot.Services;

internal sealed class HotkeySettings
{
    public string RegionKey { get; set; } = "Snapshot";
    public int RegionModifiers { get; set; } = 0;
    public string FullScreenKey { get; set; } = "D1";
    public int FullScreenModifiers { get; set; } = 6;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipShot",
        "settings.json");

    public static HotkeySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new HotkeySettings();

            var settings = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(SettingsPath))
                           ?? new HotkeySettings();

            if (!Enum.TryParse<Key>(settings.RegionKey, out var regionKey) || regionKey == Key.None ||
                !Enum.TryParse<Key>(settings.FullScreenKey, out var fullScreenKey) || fullScreenKey == Key.None)
                return new HotkeySettings();

            settings.RegionKey = regionKey.ToString();
            settings.FullScreenKey = fullScreenKey.ToString();
            settings.RegionModifiers &= 0x0F;
            settings.FullScreenModifiers &= 0x0F;
            return settings;
        }
        catch
        {
            return new HotkeySettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public HotkeySettings Copy() => new()
    {
        RegionKey = RegionKey,
        RegionModifiers = RegionModifiers,
        FullScreenKey = FullScreenKey,
        FullScreenModifiers = FullScreenModifiers
    };
}
