using System.IO;
using System.Text.Json;

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

            return JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(SettingsPath))
                   ?? new HotkeySettings();
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
