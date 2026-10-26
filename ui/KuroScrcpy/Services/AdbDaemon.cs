using System.Diagnostics;
using System.IO;

namespace KuroScrcpy.Services;

/// <summary>
/// Chay "adb -a nodaemon server start": mo adb server tren 0.0.0.0:5037 de may khac trong LAN
/// (vd PC chay KuroScrcpy) dung duoc dien thoai dang cam vao may nay.
/// </summary>
public sealed class AdbDaemon : IDisposable
{
    private readonly AdbService _adb;
    private Process? _process;

    public AdbDaemon(AdbService adb) => _adb = adb;

    public bool IsRunning => _process is { HasExited: false };

    public string LastError { get; private set; } = "";

    public async Task<bool> StartAsync()
    {
        LastError = "";

        if (!File.Exists(_adb.AdbPath))
        {
            LastError = $"không thấy adb.exe tại {_adb.AdbPath}";
            return false;
        }

        // Server cu chi listen 127.0.0.1 -> phai tat truoc khi mo lai tren 0.0.0.0
        await RunAsync("kill-server", 8000);
        await Task.Delay(300);

        var psi = new ProcessStartInfo(_adb.AdbPath, "-a nodaemon server start")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _adb.ScrcpyDir,
        };

        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }

        // Cho server that su lang nghe va tra loi handshake adb
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(200);

            if (_process == null || _process.HasExited)
            {
                LastError = "adb daemon thoát ngay (cổng 5037 có thể đang bị chiếm)";
                return false;
            }

            if (await LanScan.IsAdbServerAsync("127.0.0.1")) return true;
        }

        LastError = "adb daemon không mở được cổng 5037";
        return false;
    }

    public async Task StopAsync()
    {
        // kill-server tat ca server dang listen (ke ca daemon), roi don process con lai
        await RunAsync("kill-server", 8000);

        try
        {
            if (_process is { HasExited: false }) _process.Kill(true);
        }
        catch
        {
            // da tat
        }

        _process?.Dispose();
        _process = null;
    }

    private async Task RunAsync(string arguments, int timeoutMs)
    {
        if (!File.Exists(_adb.AdbPath)) return;

        var psi = new ProcessStartInfo(_adb.AdbPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _adb.ScrcpyDir,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return;

            var exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(timeoutMs)) != exit)
            {
                try { process.Kill(true); } catch { /* ignore */ }
            }
        }
        catch
        {
            // khong critical
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(true);
        }
        catch
        {
            // ignore
        }

        _process?.Dispose();
        _process = null;
    }
}
