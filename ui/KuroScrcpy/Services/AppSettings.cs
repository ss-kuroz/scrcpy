using System.IO;
using System.Text.Json;

namespace KuroScrcpy.Services;

public class AppSettings
{
    public string ScrcpyDir { get; set; } = "";
    public int MaxFps { get; set; } = 60;
    public string BitRate { get; set; } = "8M";
    public string AudioMode { get; set; } = "PcOnly"; // Off | PcOnly | Both
    public bool PcLandscape { get; set; } = true;
    public bool AutoConnectWireless { get; set; } = true;
    public bool AllowControl { get; set; } = false;

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "KuroScrcpy", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.ScrcpyDir) || !File.Exists(Path.Combine(loaded.ScrcpyDir, "scrcpy.exe")))
                        loaded.ScrcpyDir = ResolveScrcpyDir();
                    return loaded;
                }
            }
        }
        catch
        {
            // settings hong -> dung mac dinh
        }

        var settings = new AppSettings { ScrcpyDir = ResolveScrcpyDir() };
        return settings;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // khong critical
        }
    }

    /// <summary>Tim thu muc chua scrcpy.exe: canh app, hoac dist/ o cac cap tren.</summary>
    public static string ResolveScrcpyDir()
    {
        var env = Environment.GetEnvironmentVariable("SCRCPY_DIR");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "scrcpy.exe"))) return env;

        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "scrcpy.exe"))) return baseDir;

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var direct = Path.Combine(dir.FullName, "scrcpy.exe");
            if (File.Exists(direct)) return dir.FullName;

            var dist = Path.Combine(dir.FullName, "dist");
            if (File.Exists(Path.Combine(dist, "scrcpy.exe"))) return dist;

            dir = dir.Parent;
        }

        return baseDir;
    }
}
