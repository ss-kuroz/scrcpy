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

    /// <summary>Dia chi wireless da tung ket noi (ip:port), de connect lai khi mdns khong quang ba.</summary>
    public List<string> KnownAddresses { get; set; } = new();

    private static string? _resolvedPath;

    private static string PrimaryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "KuroScrcpy", "settings.json");

    private static string FallbackPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static string SettingsPath => _resolvedPath ?? PrimaryPath;

    public static AppSettings Load()
    {
        foreach (var path in new[] { SettingsPath, FallbackPath })
        {
            try
            {
                if (!File.Exists(path)) continue;

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded == null) continue;

                _resolvedPath = path;
                if (string.IsNullOrWhiteSpace(loaded.ScrcpyDir) || !File.Exists(Path.Combine(loaded.ScrcpyDir, "scrcpy.exe")))
                    loaded.ScrcpyDir = ResolveScrcpyDir();
                return loaded;
            }
            catch
            {
                // file hong -> thu path khac
            }
        }

        return new AppSettings { ScrcpyDir = ResolveScrcpyDir() };
    }

    public void Save()
    {
        foreach (var path in new[] { SettingsPath, FallbackPath })
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                _resolvedPath = path;
                return;
            }
            catch
            {
                // khong ghi duoc -> thu path tiep theo
            }
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
