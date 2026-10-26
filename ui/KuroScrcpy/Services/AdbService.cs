using System.Diagnostics;
using System.IO;
using System.Text;

namespace KuroScrcpy.Services;

public record AdbResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Combined => (Output + "\n" + Error).Trim();
    public override string ToString() => Combined;
}

public record AdbDevice(string Serial, string State, string Model, string Product, string TransportId, string HardwareId)
{
    /// <summary>USB > ip:port > mDNS (u tien ket noi on dinh hon).</summary>
    public int Preference => Serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase) ? 2
                           : Serial.Contains(':') ? 1
                           : 0;
}

public record MdnsService(string Name, string Type, string Address)
{
    public bool IsPairing => Type.Contains("pairing", StringComparison.OrdinalIgnoreCase);
    public bool IsConnect => Type.Contains("connect", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Wrapper mong quanh adb.exe: chay lenh, liet ke thiet bi, pair/connect.</summary>
public class AdbService
{
    public string ScrcpyDir { get; set; } = "";
    public string AdbPath => Path.Combine(ScrcpyDir, "adb.exe");

    public bool IsReady => File.Exists(AdbPath);

    public async Task<AdbResult> RunAsync(string arguments, int timeoutMs = 20000)
    {
        if (!IsReady)
        {
            return new AdbResult(-1, "", $"Khong tim thay adb.exe tai {AdbPath}");
        }

        var psi = new ProcessStartInfo(AdbPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ScrcpyDir,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new AdbResult(-1, "", ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* ignore */ }
            return new AdbResult(-1, stdout.ToString(), stderr + "\n(timeout)");
        }

        return new AdbResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task<List<AdbDevice>> GetDevicesAsync()
    {
        var result = await RunAsync("devices -l", 20000);
        var devices = new List<AdbDevice>();
        if (!result.Ok && string.IsNullOrWhiteSpace(result.Output)) return devices;

        foreach (var raw in result.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("*", StringComparison.Ordinal)) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var serial = parts[0];
            var state = parts[1];
            var model = "";
            var product = "";
            var transport = "";
            var hardware = "";
            for (var i = 2; i < parts.Length; i++)
            {
                var token = parts[i];
                if (token.StartsWith("model:", StringComparison.Ordinal)) model = token[6..];
                else if (token.StartsWith("product:", StringComparison.Ordinal)) product = token[8..];
                else if (token.StartsWith("transport_id:", StringComparison.Ordinal)) transport = token[13..];
                else if (token.StartsWith("device:", StringComparison.Ordinal)) hardware = token[7..];
            }

            devices.Add(new AdbDevice(serial, state, model, product, transport, hardware));
        }

        return devices;
    }

    public async Task<List<MdnsService>> GetMdnsServicesAsync()
    {
        var result = await RunAsync("mdns services", 8000);
        var services = new List<MdnsService>();
        foreach (var raw in result.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of discovered", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            services.Add(new MdnsService(parts[0], parts[1], parts[2]));
        }
        return services;
    }

    public Task<AdbResult> PairAsync(string address, string code) =>
        RunAsync($"pair {address} {code}", 35000);

    public Task<AdbResult> ConnectAsync(string address) =>
        RunAsync($"connect {address}", 20000);

    public Task<AdbResult> DisconnectAsync(string address) =>
        RunAsync($"disconnect {address}", 10000);

    public async Task<string> ShellAsync(string serial, string command)
    {
        var result = await RunAsync($"-s \"{serial}\" shell {command}", 15000);
        return result.Combined;
    }

    /// <summary>Doc kich thuoc man hinh dang hien tai (theo huong xoay), tra ve "WxH" hoac "".</summary>
    public async Task<string> GetScreenSizeAsync(string serial)
    {
        // cur= trong dumpsys window phan anh dung huong dang xoay (wm size chi tra ve physical)
        var displays = await ShellAsync(serial, "dumpsys window displays");
        var match = System.Text.RegularExpressions.Regex.Match(displays, @"cur=(\d+)x(\d+)");
        if (match.Success && match.Groups[1].Value != "0")
        {
            return $"{match.Groups[1].Value}×{match.Groups[2].Value}";
        }

        var output = await ShellAsync(serial, "wm size");
        string? physical = null;
        string? over = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Physical size:", StringComparison.OrdinalIgnoreCase))
                physical = line["Physical size:".Length..].Trim();
            else if (line.StartsWith("Override size:", StringComparison.OrdinalIgnoreCase))
                over = line["Override size:".Length..].Trim();
        }

        var size = over ?? physical;
        if (string.IsNullOrWhiteSpace(size)) return "";
        return size.Replace(" ", "").Replace('x', '×');
    }

    /// <summary>Ten hien thi cua dien thoai (Settings -> Device name).</summary>
    public async Task<string> GetDeviceNameAsync(string serial)
    {
        var output = await ShellAsync(serial, "settings get global device_name");
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Equals("null", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Error", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("Exception", StringComparison.Ordinal)) continue;
            return line;
        }
        return "";
    }

    /// <summary>Danh thuc man hinh + bo qua keyguard (khong lam gi neu may dang sang).</summary>
    public Task<AdbResult> WakeAsync(string serial) =>
        RunAsync($"-s \"{serial}\" shell \"input keyevent KEYCODE_WAKEUP; wm dismiss-keyguard\"", 10000);

    /// <summary>Khoa cung huong ngang (landscape). Android 13+ can them fixed-to-user-rotation.</summary>
    public Task<AdbResult> LockLandscapeAsync(string serial) =>
        RunAsync($"-s \"{serial}\" shell \"wm fixed-to-user-rotation enabled; settings put system accelerometer_rotation 0; settings put system user_rotation 1; wm user-rotation lock 1\"", 15000);

    public Task<AdbResult> UnlockRotationAsync(string serial) =>
        RunAsync($"-s \"{serial}\" shell \"wm fixed-to-user-rotation default; settings put system accelerometer_rotation 1; wm user-rotation free\"", 15000);
}
