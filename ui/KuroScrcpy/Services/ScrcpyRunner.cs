using System.Diagnostics;
using System.IO;
using KuroScrcpy.Models;

namespace KuroScrcpy.Services;

public enum AudioMode
{
    Off,
    PcOnly,
    Both,
}

public class MirrorOptions
{
    public int MaxFps { get; set; } = 60;
    public string BitRate { get; set; } = "8M";
    public AudioMode Audio { get; set; } = AudioMode.PcOnly;
    public bool AllowControl { get; set; }
    public bool LandscapeLock { get; set; } = true;
}

public class MirrorSession
{
    public required DeviceEntry Device { get; init; }
    public required Process Process { get; init; }
    public required string WindowTitle { get; init; }
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public bool HasTarget { get; set; }
    public int SlotX { get; set; }
    public int SlotY { get; set; }
    /// <summary>Thu tu cua so trong lan chay nay, dung de lech nhe khi nhieu may cung center.</summary>
    public int CascadeIndex { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>Chay scrcpy.exe cho tung thiet bi va khoa cua so ve dung resolution goc.</summary>
public class ScrcpyRunner
{
    private readonly AdbService _adb;
    private readonly Action<string> _log;
    private readonly List<MirrorSession> _sessions = new();
    private readonly object _gate = new();

    public event Action<string>? SessionEnded;
    public event Action? LayoutChanged;

    public ScrcpyRunner(AdbService adb, Action<string> log)
    {
        _adb = adb;
        _log = log;
    }

    public bool IsRunning(string serial)
    {
        lock (_gate) return _sessions.Any(s => s.Device.Serial == serial);
    }

    public IReadOnlyList<MirrorSession> Sessions
    {
        get { lock (_gate) return _sessions.ToList(); }
    }

    public async Task StartAsync(DeviceEntry device, MirrorOptions options)
    {
        if (IsRunning(device.Serial))
        {
            _log($"[{device.Title}] đang mirror rồi");
            return;
        }

        var scrcpyExe = Path.Combine(_adb.ScrcpyDir, "scrcpy.exe");
        if (!File.Exists(scrcpyExe))
        {
            _log($"Không thấy scrcpy.exe tại {scrcpyExe}");
            return;
        }

        if (options.LandscapeLock)
        {
            var rotation = await _adb.LockLandscapeAsync(device.Serial);
            _log($"[{device.Title}] khoá landscape: {rotation.Combined}");
        }

        // Man hinh dang dozing thi scrcpy chi thay khung den
        await _adb.WakeAsync(device.Serial);

        // Kich thuoc native theo huong hien tai: truyen thang de scrcpy khong scale cho vua man hinh PC
        var nativeWidth = 0;
        var nativeHeight = 0;
        var sizeText = await _adb.GetScreenSizeAsync(device.Serial);
        if (TryParseSize(sizeText, out var width, out var height))
        {
            nativeWidth = width;
            nativeHeight = height;
            _log($"[{device.Title}] resolution gốc {nativeWidth}×{nativeHeight}");
        }

        var title = $"KuroScrcpy|{device.Title}|{device.Serial}";
        var args = new List<string>
        {
            "-s", Quote(device.Serial),
            "--render-fit=unscaled",
            $"--window-title={Quote(title)}",
        };

        if (nativeWidth > 0 && nativeHeight > 0)
        {
            args.Add($"--window-width={nativeWidth}");
            args.Add($"--window-height={nativeHeight}");
        }

        if (!options.AllowControl) args.Add("--no-control");
        if (options.MaxFps > 0) args.Add($"--max-fps={options.MaxFps}");
        if (!string.IsNullOrWhiteSpace(options.BitRate)) args.Add($"--video-bit-rate={options.BitRate}");

        switch (options.Audio)
        {
            case AudioMode.Off:
                args.Add("--no-audio");
                break;
            case AudioMode.PcOnly:
                break; // mac dinh: output -> PC, tat loa may
            case AudioMode.Both:
                args.Add("--audio-dup");
                break;
        }

        var psi = new ProcessStartInfo(scrcpyExe, string.Join(" ", args))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _adb.ScrcpyDir,
        };

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log($"[{device.Title}] {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log($"[{device.Title}] {e.Data}"); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _log($"Không chạy được scrcpy: {ex.Message}");
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var session = new MirrorSession
        {
            Device = device,
            Process = process,
            WindowTitle = title,
            CascadeIndex = Sessions.Count,
        };

        lock (_gate) _sessions.Add(session);
        _log($"[{device.Title}] bắt đầu mirror (pid {process.Id})");
        LayoutChanged?.Invoke();

        _ = Task.Run(() => WatchAsync(session));
    }

    public void Stop(string serial)
    {
        MirrorSession? session;
        lock (_gate) session = _sessions.FirstOrDefault(s => s.Device.Serial == serial);
        if (session == null) return;

        try
        {
            if (!session.Process.HasExited) session.Process.Kill(true);
        }
        catch (Exception ex)
        {
            _log($"[{session.Device.Title}] lỗi khi dừng: {ex.Message}");
        }

        RemoveSession(session, "đã dừng");
    }

    public void StopAll()
    {
        foreach (var session in Sessions) Stop(session.Device.Serial);
    }

    public void Arrange()
    {
        var area = WindowTools.GetWorkArea();
        var sessions = Sessions.Where(s => s.HasTarget).OrderBy(s => s.StartedAt).ToList();
        var x = area.Left;
        var y = area.Top;
        var rowHeight = 0;

        foreach (var session in sessions)
        {
            if (x > area.Left && x + session.TargetWidth > area.Right)
            {
                x = area.Left;
                y += rowHeight;
                rowHeight = 0;
            }

            session.SlotX = x;
            session.SlotY = y;
            x += session.TargetWidth;
            rowHeight = Math.Max(rowHeight, session.TargetHeight);
        }

        foreach (var session in sessions)
        {
            var windows = WindowTools.FindScrcpyWindows(session.Process.Id);
            if (windows.Count > 0) WindowTools.MoveClientTo(windows[0], session.SlotX, session.SlotY);
        }
    }

    private async Task WatchAsync(MirrorSession session)
    {
        var lastWidth = 0;
        var lastHeight = 0;
        var stableSince = DateTime.UtcNow;
        var lastWake = DateTime.UtcNow;

        while (!session.Process.HasExited)
        {
            // Giu man hinh dien thoai thuc (neu khong se dozing -> mirror den)
            if ((DateTime.UtcNow - lastWake).TotalSeconds > 15)
            {
                lastWake = DateTime.UtcNow;
                _ = _adb.WakeAsync(session.Device.Serial);
            }
            var windows = WindowTools.FindScrcpyWindows(session.Process.Id);
            if (windows.Count > 0)
            {
                var hwnd = windows[0];
                var (width, height) = WindowTools.GetClientSize(hwnd);

                if (!session.HasTarget)
                {
                    // Cho kich thuoc on dinh (scrcpy tao window 256x256 truoc khi co frame dau)
                    if (width > 0 && height > 0)
                    {
                        if (width != lastWidth || height != lastHeight)
                        {
                            lastWidth = width;
                            lastHeight = height;
                            stableSince = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - stableSince).TotalMilliseconds > 500)
                        {
                            session.TargetWidth = width;
                            session.TargetHeight = height;
                            session.HasTarget = true;
                            _log($"[{session.Device.Title}] cửa sổ gốc {width}×{height}");
                            PlaceCentered(session, hwnd);
                        }
                    }
                }
                else
                {
                    WindowTools.LockAndResize(hwnd, session.TargetWidth, session.TargetHeight);
                }
            }

            await Task.Delay(400);
        }

        RemoveSession(session, $"thoát (exit {SafeExitCode(session.Process)})");
    }

    private void RemoveSession(MirrorSession session, string reason)
    {
        bool removed;
        lock (_gate) removed = _sessions.Remove(session);
        if (!removed) return;

        _log($"[{session.Device.Title}] {reason}");
        SessionEnded?.Invoke(session.Device.Serial);
        LayoutChanged?.Invoke();
    }

    /// <summary>Dat nguyen cua so (ke ca titlebar) o giua work area; nhieu may lech nhau 36px.</summary>
    private void PlaceCentered(MirrorSession session, IntPtr hwnd)
    {
        var area = WindowTools.GetWorkArea();
        var (windowWidth, windowHeight) = WindowTools.GetWindowSize(hwnd);
        if (windowWidth <= 0 || windowHeight <= 0) return;

        var offset = Math.Max(0, session.CascadeIndex) * 36;
        var maxX = Math.Max(area.Left, area.Right - windowWidth);
        var maxY = Math.Max(area.Top, area.Bottom - windowHeight);

        var x = area.Left + (area.Width - windowWidth) / 2 + offset;
        var y = area.Top + (area.Height - windowHeight) / 2 + offset;

        x = Math.Clamp(x, area.Left, maxX);
        y = Math.Clamp(y, area.Top, maxY);

        session.SlotX = x;
        session.SlotY = y;
        WindowTools.MoveWindowTo(hwnd, x, y);
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Replace('×', 'x').Trim();
        var parts = normalized.Split('x');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
               && width > 0 && height > 0;
    }
}
