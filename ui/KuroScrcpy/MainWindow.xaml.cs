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
    private readonly ScrcpyRunner _runner;
    private readonly ObservableCollection<DeviceEntry> _devices = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _lastConnectAttempt = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _adb.ScrcpyDir = _settings.ScrcpyDir;
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

    // ---------- settings <-> UI ----------

    private void ApplySettingsToUi()
    {
        BitRateBox.Text = _settings.BitRate;
        ChkLandscape.IsChecked = _settings.LandscapeLock;
        ChkControl.IsChecked = _settings.AllowControl;
        ChkAutoConnect.IsChecked = _settings.AutoConnectWireless;
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
        _settings.LandscapeLock = ChkLandscape.IsChecked == true;
        _settings.AllowControl = ChkControl.IsChecked == true;
        _settings.AutoConnectWireless = ChkAutoConnect.IsChecked == true;
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

            var devices = await _adb.GetDevicesAsync();
            var mdns = await _adb.GetMdnsServicesAsync();

            // Mot may co the hien thi thanh nhieu transport (USB / ip:port / mDNS) -> gop theo hardware id
            devices = devices
                .GroupBy(d => string.IsNullOrEmpty(d.HardwareId) ? d.Serial : d.HardwareId)
                .Select(g => g.OrderBy(d => d.Preference).First())
                .ToList();

            foreach (var device in devices)
            {
                var entry = _devices.FirstOrDefault(d => d.Serial == device.Serial);
                if (entry == null)
                {
                    entry = new DeviceEntry { Serial = device.Serial };
                    _devices.Add(entry);
                }

                if (!string.IsNullOrWhiteSpace(device.Model)) entry.Model = device.Model;
                entry.State = device.State;

                if (entry.State == "device" && string.IsNullOrEmpty(entry.Resolution))
                {
                    _ = LoadResolutionAsync(entry.Serial);
                }

                if (entry.State == "device" && string.IsNullOrEmpty(entry.DeviceName))
                {
                    _ = LoadDeviceNameAsync(entry.Serial);
                }
            }

            if (_settings.AutoConnectWireless)
            {
                foreach (var service in mdns.Where(s => s.IsConnect))
                {
                    if (devices.Any(d => d.Serial == service.Address)) continue;
                    if (_lastConnectAttempt.TryGetValue(service.Address, out var last) &&
                        (DateTime.UtcNow - last).TotalSeconds < 20) continue;

                    _lastConnectAttempt[service.Address] = DateTime.UtcNow;
                    AppendLog($"Tự connect {service.Address}…");
                    _ = Task.Run(async () =>
                    {
                        var result = await _adb.ConnectAsync(service.Address);
                        AppendLog($"connect {service.Address}: {result.Combined}");
                    });
                }
            }

            foreach (var service in mdns.Where(s => s.IsConnect))
            {
                if (devices.Any(d => d.Serial == service.Address)) continue;
                if (_devices.Any(d => d.Serial == service.Address)) continue;
                _devices.Add(new DeviceEntry
                {
                    Serial = service.Address,
                    Model = service.Name,
                    Address = service.Address,
                    State = "discovered"
                });
            }

            var liveSerials = devices.Select(d => d.Serial).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var liveAddresses = mdns.Select(s => s.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _devices.ToList())
            {
                if (entry.IsMirroring) continue;
                var alive = entry.IsAdbDevice ? liveSerials.Contains(entry.Serial) : liveAddresses.Contains(entry.Serial);
                if (!alive) _devices.Remove(entry);
            }

            var ready = devices.Count(d => d.State == "device");
            var pairing = mdns.Count(s => s.IsPairing);
            SubtitleText.Text = $"{ready} thiết bị sẵn sàng  •  {devices.Count} kết nối adb  •  {pairing} dịch vụ pairing";
            StatusText.Text = _adb.ScrcpyDir;

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

    private async Task LoadResolutionAsync(string serial)
    {
        var size = await _adb.GetScreenSizeAsync(serial);
        if (string.IsNullOrWhiteSpace(size)) return;
        await Dispatcher.InvokeAsync(() =>
        {
            var entry = _devices.FirstOrDefault(d => d.Serial == serial);
            if (entry != null) entry.Resolution = size;
        });
    }

    private async Task LoadDeviceNameAsync(string serial)
    {
        var name = await _adb.GetDeviceNameAsync(serial);
        if (string.IsNullOrWhiteSpace(name)) return;
        await Dispatcher.InvokeAsync(() =>
        {
            var entry = _devices.FirstOrDefault(d => d.Serial == serial);
            if (entry != null) entry.DeviceName = name;
        });
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
            LandscapeLock = ChkLandscape.IsChecked == true,
        };

        device.IsMirroring = true;
        UpdateButtons();

        await _runner.StartAsync(device, options);

        if (!_runner.IsRunning(device.Serial))
        {
            device.IsMirroring = false;
            UpdateButtons();
        }
        else
        {
            // Sau khi khoá landscape, resolution hien tai co the khac luc quet -> doc lai
            device.Resolution = "";
            await LoadResolutionAsync(device.Serial);
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

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;
        var address = string.IsNullOrWhiteSpace(device.Address) ? device.Serial : device.Address;
        AppendLog($"adb connect {address}…");
        var result = await _adb.ConnectAsync(address);
        AppendLog($"connect {address}: {result.Combined}");
        await RefreshAsync();
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var device = Selected;
        if (device == null) return;
        var address = string.IsNullOrWhiteSpace(device.Address) ? device.Serial : device.Address;
        var result = await _adb.DisconnectAsync(address);
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
