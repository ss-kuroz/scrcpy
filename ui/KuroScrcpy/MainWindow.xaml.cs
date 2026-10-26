using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KuroScrcpy.Models;
using KuroScrcpy.Services;
using Microsoft.Win32;

namespace KuroScrcpy;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AdbService _adb = new();
    private AdbService? _remote;
    private readonly ScrcpyRunner _runner;
    private readonly ObservableCollection<DeviceEntry> _devices = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _lastConnectAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _connectFailed = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshing;
    private DateTime? _emptySince;
    private DateTime _lastServerRestart = DateTime.MinValue;
    private bool _remoteWarned;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _adb.ScrcpyDir = _settings.ScrcpyDir;
        if (!string.IsNullOrWhiteSpace(_settings.AdbServerHost))
        {
            _remote = new AdbService
            {
                ScrcpyDir = _settings.ScrcpyDir,
                ServerAddress = _settings.AdbServerHost,
            };
        }
        _runner = new ScrcpyRunner(_adb, AppendLog);

        DevicesList.ItemsSource = _devices;
        ApplySettingsToUi();

        _runner.SessionEnded += serial => Dispatcher.BeginInvoke(() =>
        {
            var entry = _devices.FirstOrDefault(d => d.Serial == serial);
            if (entry != null) entry.IsMirroring = false;
            UpdateButtons();
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += async (_, _) =>
        {
            AppendLog($"scrcpy dir: {_adb.ScrcpyDir}");
            await RefreshAsync();
            _timer.Start();
        };

        SourceInitialized += (_, _) => UseDarkTitleBar();

        Closing += (_, _) =>
        {
            SaveUiToSettings();
            _settings.Save();
            _runner.StopAll();
        };
    }

    private DeviceEntry? Selected => DevicesList.SelectedItem as DeviceEntry;

    /// <summary>Cac nguon adb: luon co may nay, them laptop neu da cau hinh.</summary>
    private IEnumerable<AdbService> Sources
    {
        get
        {
            yield return _adb;
            if (_remote != null) yield return _remote;
        }
    }

    /// <summary>adb server phu trach mot thiet bi (local hoac laptop).</summary>
    private AdbService SourceFor(DeviceEntry? device)
    {
        if (device != null && _remote != null &&
            string.Equals(device.SourceAddress, _remote.ServerAddress, StringComparison.OrdinalIgnoreCase))
        {
            return _remote;
        }
        return _adb;
    }

    private static string DeviceKey(AdbDevice device) =>
        string.IsNullOrEmpty(device.HardwareId) ? "serial:" + device.Serial : "hw:" + device.HardwareId;

    /// <summary>Serial USB la serial phan cung; wireless la ip:port hoac ten mDNS adb-...</summary>
    private static bool IsUsbSerial(string serial) =>
        !serial.Contains(':') && !serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase);

    private static int SourcePreference(AdbService source, AdbDevice device)
    {
        var usb = IsUsbSerial(device.Serial);
        if (!source.IsRemote) return usb ? 0 : 2;
        return usb ? 1 : 3;
    }

    // ---------- settings <-> UI ----------

    private void ApplySettingsToUi()
    {
        BitRateBox.Text = _settings.BitRate;
        ChkControl.IsChecked = _settings.AllowControl;
        ChkAutoConnect.IsChecked = _settings.AutoConnectWireless;
        RemoteAdbBox.Text = _settings.AdbServerHost;
        PathText.Text = _adb.ScrcpyDir;

        switch (_settings.MaxFps)
        {
            case 30: RbFps30.IsChecked = true; break;
            case 60: RbFps60.IsChecked = true; break;
            case 90: RbFps90.IsChecked = true; break;
            case 120: RbFps120.IsChecked = true; break;
            default: RbFpsMax.IsChecked = true; break;
        }

        switch (_settings.AudioMode)
        {
            case "Off": RbAudioOff.IsChecked = true; break;
            case "Both": RbAudioBoth.IsChecked = true; break;
            default: RbAudioPc.IsChecked = true; break;
        }
    }

    private void SaveUiToSettings()
    {
        _settings.ScrcpyDir = _adb.ScrcpyDir;
        _settings.MaxFps = SelectedFps();
        _settings.BitRate = BitRateBox.Text.Trim();
        _settings.AudioMode = SelectedAudio() switch
        {
            AudioMode.Off => "Off",
            AudioMode.Both => "Both",
            _ => "PcOnly"
        };
        _settings.AllowControl = ChkControl.IsChecked == true;
        _settings.AutoConnectWireless = ChkAutoConnect.IsChecked == true;
        _settings.AdbServerHost = RemoteAdbBox.Text.Trim();
    }

    private int SelectedFps()
    {
        if (RbFps30.IsChecked == true) return 30;
        if (RbFps60.IsChecked == true) return 60;
        if (RbFps90.IsChecked == true) return 90;
        if (RbFps120.IsChecked == true) return 120;
        return 0;
    }

    private AudioMode SelectedAudio()
    {
        if (RbAudioOff.IsChecked == true) return AudioMode.Off;
        if (RbAudioBoth.IsChecked == true) return AudioMode.Both;
        return AudioMode.PcOnly;
    }

    // ---------- device polling ----------

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            if (!_adb.IsReady)
            {
                StatusText.Text = $"Không thấy adb.exe trong {_adb.ScrcpyDir}";
                return;
            }

            // Lay thiet bi tu tat ca nguon: adb tren may nay + adb server tu xa (laptop dang cam may)
            var collected = new List<(AdbService Source, AdbDevice Device)>();
            var remoteFailed = false;

            foreach (var source in Sources)
            {
                var list = await source.GetDevicesAsync();
                if (source.IsRemote && list.Count == 0)
                {
                    remoteFailed = true;
                    if (!_remoteWarned)
                    {
                        _remoteWarned = true;
                        var probe = await source.RunAsync("devices", 5000);
                        AppendLog($"Không thấy thiết bị từ adb server {source.ServerAddress}: {probe.Combined}");
                    }
                }
                collected.AddRange(list.Select(d => (source, d)));
            }

            if (!remoteFailed) _remoteWarned = false;

            var rawDevices = collected.Where(x => !x.Source.IsRemote).Select(x => x.Device).ToList();
            var mdns = await _adb.GetMdnsServicesAsync();

            // Gop cung mot may (USB / wireless / nhieu may tinh) theo hardware id.
            // Uu tien: USB may nay > USB may khac trong LAN > wireless may nay > wireless may khac.
            var chosen = collected
                .GroupBy(x => DeviceKey(x.Device))
                .Select(g => g.OrderBy(x => SourcePreference(x.Source, x.Device))
                              .ThenBy(x => x.Device.Preference)
                              .First())
                .ToList();

            foreach (var (source, device) in chosen)
            {
                var entry = _devices.FirstOrDefault(d => d.Serial == device.Serial)
                    ?? (string.IsNullOrEmpty(device.HardwareId)
                        ? null
                        : _devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.HardwareId) && d.HardwareId == device.HardwareId));

                if (entry == null)
                {
                    entry = new DeviceEntry();
                    _devices.Add(entry);
                }

                // Dang mirror thi giu serial/nguon cua phien dang chay
                if (!entry.IsMirroring)
                {
                    entry.Serial = device.Serial;
                    entry.SourceAddress = source.ServerAddress;
                    entry.IsUsb = IsUsbSerial(device.Serial);
                }

                entry.HardwareId = device.HardwareId;
                if (!string.IsNullOrWhiteSpace(device.Model)) entry.Model = device.Model;
                entry.State = device.State;

                if (entry.State == "device" && string.IsNullOrEmpty(entry.Resolution))
                {
                    _ = LoadResolutionAsync(entry, SourceFor(entry));
                }

                if (entry.State == "device" && string.IsNullOrEmpty(entry.DeviceName))
                {
                    _ = LoadDeviceNameAsync(entry, SourceFor(entry));
                }
            }

            // Nho dia chi wireless da ket noi (de connect lai khi mdns khong quang ba)
            var remembered = false;
            foreach (var raw in _adb.IsRemote
                         ? Enumerable.Empty<AdbDevice>()
                         : rawDevices.Where(d => d.State == "device" && d.Serial.Contains(':')))
            {
                if (_settings.KnownAddresses.Contains(raw.Serial, StringComparer.OrdinalIgnoreCase)) continue;
                _settings.KnownAddresses.Add(raw.Serial);
                remembered = true;
            }
            if (remembered) _settings.Save();

            var candidates = mdns.Where(s => s.IsConnect).Select(s => s.Address)
                                 .Concat(_adb.IsRemote ? Enumerable.Empty<string>() : _settings.KnownAddresses)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            // Recovery: mDNS cua adb hay bi ket, nhat la sau khi dien thoai reboot (port wireless doi).
            // Chi restart khi khong mirror de khong cat phien dang chay.
            var noSignal = rawDevices.Count == 0 && mdns.Count == 0;
            var staleReachable = false;
            if (!_adb.IsRemote && _runner.Sessions.Count == 0)
            {
                foreach (var address in _settings.KnownAddresses)
                {
                    if (rawDevices.Any(d => d.Serial.Equals(address, StringComparison.OrdinalIgnoreCase))) continue;
                    if (mdns.Any(s => s.Address.Equals(address, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!_connectFailed.TryGetValue(address, out var failedAt)) continue;
                    if ((DateTime.UtcNow - failedAt).TotalSeconds > 40) continue;
                    if (await IsTcpOpenAsync(address)) { staleReachable = true; break; }
                }
            }

            if (!_adb.IsRemote && _runner.Sessions.Count == 0 && (staleReachable || noSignal))
            {
                _emptySince ??= DateTime.UtcNow;
                var waited = (DateTime.UtcNow - _emptySince.Value).TotalSeconds;
                if ((staleReachable || waited > 20) && (DateTime.UtcNow - _lastServerRestart).TotalSeconds > 90)
                {
                    _lastServerRestart = DateTime.UtcNow;
                    _emptySince = null;
                    AppendLog("Thiết bị không hiện trong adb — khởi động lại adb server…");
                    var restart = await _adb.RestartServerAsync();
                    AppendLog("adb restart: " + restart.Combined);
                }
            }
            else
            {
                _emptySince = null;
            }

            if (_settings.AutoConnectWireless)
            {
                foreach (var address in candidates)
                {
                    if (rawDevices.Any(d => d.Serial == address)) continue;
                    if (_lastConnectAttempt.TryGetValue(address, out var last) &&
                        (DateTime.UtcNow - last).TotalSeconds < 30) continue;

                    _lastConnectAttempt[address] = DateTime.UtcNow;
                    AppendLog($"Tự connect {address}…");
                    _ = Task.Run(async () =>
                    {
                        var result = await _adb.ConnectAsync(address);
                        if (result.Combined.Contains("connected to", StringComparison.OrdinalIgnoreCase))
                        {
                            _connectFailed.TryRemove(address, out _);
                        }
                        else
                        {
                            _connectFailed[address] = DateTime.UtcNow;
                        }
                        AppendLog($"connect {address}: {result.Combined}");
                    });
                }
            }

            // Hien thi dia chi da biet nhung chua ket noi, de bam Connect
            foreach (var address in candidates)
            {
                if (rawDevices.Any(d => d.Serial == address)) continue;
                if (_devices.Any(d => d.Serial == address)) continue;
                _devices.Add(new DeviceEntry
                {
                    Serial = address,
                    Address = address,
                    State = "discovered"
                });
            }

            var liveSerials = chosen.Select(x => x.Device.Serial).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _devices.ToList())
            {
                if (entry.IsMirroring) continue;
                if (!entry.IsRemoteSource && _settings.KnownAddresses.Contains(entry.Serial, StringComparer.OrdinalIgnoreCase)) continue;
                var alive = entry.IsAdbDevice
                    ? liveSerials.Contains(entry.Serial)
                    : candidates.Contains(entry.Serial, StringComparer.OrdinalIgnoreCase);
                if (!alive) _devices.Remove(entry);
            }

            var ready = chosen.Count(x => x.Device.State == "device");
            var usbCount = chosen.Count(x => IsUsbSerial(x.Device.Serial));
            var pairing = mdns.Count(s => s.IsPairing);
            SubtitleText.Text = $"{ready} thiết bị sẵn sàng ({usbCount} USB)  •  {chosen.Count} kết nối adb  •  {pairing} dịch vụ pairing";
            StatusText.Text = _remote != null
                ? $"{_adb.ScrcpyDir}  •  adb: máy này + {_remote.ServerAddress}"
                : _adb.ScrcpyDir;

            if (DevicesList.SelectedItem == null && _devices.Count > 0) DevicesList.SelectedIndex = 0;

            UpdateButtons();
        }
        catch (Exception ex)
        {
            AppendLog("Lỗi refresh: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task LoadResolutionAsync(DeviceEntry device, AdbService source)
    {
        var serial = device.Serial;
        var size = await source.GetScreenSizeAsync(serial);
        if (string.IsNullOrWhiteSpace(size)) return;
        await Dispatcher.InvokeAsync(() =>
        {
            var entry = _devices.FirstOrDefault(d => d.Serial == serial);
            if (entry != null) entry.Resolution = size;
        });
    }

    private async Task LoadDeviceNameAsync(DeviceEntry device, AdbService source)
    {
        var serial = device.Serial;
        var name = await source.GetDeviceNameAsync(serial);
        if (string.IsNullOrWhiteSpace(name)) return;
        await Dispatcher.InvokeAsync(() =>
        {
            var entry = _devices.FirstOrDefault(d => d.Serial == serial);
            if (entry != null) entry.DeviceName = name;
        });
    }

    /// <summary>Kiem tra nhanh port adb wireless con mo (dien thoai con song) — dung de biet co nen restart adb server.</summary>
    private static async Task<bool> IsTcpOpenAsync(string address, int timeoutMs = 500)
    {
        var idx = address.LastIndexOf(':');
        if (idx <= 0) return false;
        var host = address[..idx];
        if (!int.TryParse(address[(idx + 1)..], out var port)) return false;

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateButtons()
    {
        var selected = Selected;
        ConnectBtn.IsEnabled = selected is { State: "discovered" or "offline" };
        DisconnectBtn.IsEnabled = selected is { IsWireless: true, State: "device" };
        StartBtn.IsEnabled = selected is { CanStart: true };
        StopBtn.IsEnabled = selected is { IsMirroring: true };
    }

    // ---------- actions ----------

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;

        if (device.State != "device")
        {
            AppendLog($"[{device.Title}] chưa sẵn sàng (state={device.State})");
            return;
        }

        var options = new MirrorOptions
        {
            MaxFps = SelectedFps(),
            BitRate = BitRateBox.Text.Trim(),
            Audio = SelectedAudio(),
            AllowControl = ChkControl.IsChecked == true,
        };

        var source = SourceFor(device);
        device.IsMirroring = true;
        UpdateButtons();

        await _runner.StartAsync(device, options, source);

        if (!_runner.IsRunning(device.Serial))
        {
            device.IsMirroring = false;
            UpdateButtons();
        }
        else
        {
            // Sau khi khoá landscape, resolution hien tai co the khac luc quet -> doc lai
            device.Resolution = "";
            await LoadResolutionAsync(device, source);
        }
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;
        _runner.Stop(device.Serial);
        device.IsMirroring = false;
        UpdateButtons();
    }

    private void StopAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _runner.StopAll();
        foreach (var device in _devices) device.IsMirroring = false;
        UpdateButtons();
    }

    private void ArrangeBtn_Click(object sender, RoutedEventArgs e) => _runner.Arrange();

    private async void RemoteAdbBtn_Click(object sender, RoutedEventArgs e)
    {
        var address = RemoteAdbBox.Text.Trim();
        _settings.AdbServerHost = address;
        _settings.Save();
        _remoteWarned = false;

        if (address.Length == 0)
        {
            _remote = null;
            AppendLog("ADB laptop: tắt — chỉ dùng adb trên máy này");
        }
        else
        {
            _remote = new AdbService
            {
                ScrcpyDir = _adb.ScrcpyDir,
                ServerAddress = address,
            };
            AppendLog($"ADB laptop: {AdbService.NormalizeServer(address)} — danh sách gộp máy này + laptop");
        }

        await RefreshAsync();
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;
        var address = string.IsNullOrWhiteSpace(device.Address) ? device.Serial : device.Address;
        var source = SourceFor(device);
        AppendLog($"adb connect {address}…");
        var result = await source.ConnectAsync(address);
        AppendLog($"connect {address}: {result.Combined}");
        if (result.Combined.Contains("connected to", StringComparison.OrdinalIgnoreCase) && !device.IsRemoteSource)
        {
            RememberAddress(address);
        }
        await RefreshAsync();
    }

    private async void ManualConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var address = ManualAddressBox.Text.Trim();
        if (address.Length == 0) return;

        AppendLog($"adb connect {address}…");
        var result = await _adb.ConnectAsync(address);
        AppendLog($"connect {address}: {result.Combined}");

        if (result.Combined.Contains("connected to", StringComparison.OrdinalIgnoreCase))
        {
            RememberAddress(address);
            ManualAddressBox.Text = "";
        }

        await RefreshAsync();
    }

    private void RememberAddress(string address)
    {
        if (_adb.IsRemote) return;
        if (string.IsNullOrWhiteSpace(address) || !address.Contains(':')) return;
        if (_settings.KnownAddresses.Contains(address, StringComparer.OrdinalIgnoreCase)) return;
        _settings.KnownAddresses.Add(address);
        _settings.Save();
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;
        var address = string.IsNullOrWhiteSpace(device.Address) ? device.Serial : device.Address;
        var result = await SourceFor(device).DisconnectAsync(address);
        AppendLog($"disconnect {address}: {result.Combined}");
        await RefreshAsync();
    }

    private async void PairBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PairDialog(_adb) { Owner = this };
        if (dialog.ShowDialog() == true) await RefreshAsync();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn scrcpy.exe",
            Filter = "scrcpy.exe|scrcpy.exe|Tất cả|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            var dir = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(dir)) return;
            _adb.ScrcpyDir = dir;
            if (_remote != null) _remote.ScrcpyDir = dir;
            _settings.ScrcpyDir = dir;
            _settings.Save();
            PathText.Text = dir;
            AppendLog($"scrcpy dir: {dir}");
        }
    }

    private void DevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    /// <summary>Titlebar toi cho hop theme (Windows 10 2004+).</summary>
    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var value = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
            }
        }
        catch
        {
            // khong ho tro -> bo qua
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---------- logging ----------

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(message));
            return;
        }

        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");

        if (LogBox.LineCount > 1500)
        {
            var lines = LogBox.Text.Split('\n');
            LogBox.Text = string.Join('\n', lines.Skip(lines.Length - 1000));
        }

        LogBox.ScrollToEnd();
    }
}
