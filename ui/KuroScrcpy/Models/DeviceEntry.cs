using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace KuroScrcpy.Models;

/// <summary>Mot thiet bi trong danh sach: co the la adb device, hoac mot dia chi wireless chua connect.</summary>
public class DeviceEntry : INotifyPropertyChanged
{
    private string _serial = "";
    private string _model = "";
    private string _deviceName = "";
    private string _state = "";
    private string _resolution = "";
    private string _hardwareId = "";
    private string _sourceAddress = "";
    private bool _isUsb;
    private bool _mirroring;
    private string _address = "";

    public string Serial
    {
        get => _serial;
        set { if (Set(ref _serial, value)) { Notify(nameof(Subtitle)); Notify(nameof(ConnectionLabel)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionHint)); } }
    }

    public string Model
    {
        get => _model;
        set { if (Set(ref _model, value)) { Notify(nameof(Title)); Notify(nameof(Subtitle)); } }
    }

    /// <summary>Ten hien thi cua dien thoai (Settings -> Device name), vd "realme 15T".</summary>
    public string DeviceName
    {
        get => _deviceName;
        set { if (Set(ref _deviceName, value)) { Notify(nameof(Title)); Notify(nameof(Subtitle)); } }
    }

    /// <summary>device | unauthorized | offline | discovered | pairing</summary>
    public string State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Notify(nameof(StateText)); Notify(nameof(StatusBrush)); Notify(nameof(Subtitle)); Notify(nameof(CanStart)); Notify(nameof(ConnectionLabel)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionHint)); } }
    }

    public string Resolution
    {
        get => _resolution;
        set { if (Set(ref _resolution, value)) Notify(nameof(Subtitle)); }
    }

    public bool IsMirroring
    {
        get => _mirroring;
        set { if (Set(ref _mirroring, value)) { Notify(nameof(StateText)); Notify(nameof(StatusBrush)); Notify(nameof(Subtitle)); Notify(nameof(CanStart)); } }
    }

    /// <summary>ip:port cho mdns service (dung de connect/pair).</summary>
    public string Address
    {
        get => _address;
        set => Set(ref _address, value);
    }

    /// <summary>hardware id cua may (adb device:...), de gop cung mot may giua USB va wireless.</summary>
    public string HardwareId
    {
        get => _hardwareId;
        set => Set(ref _hardwareId, value);
    }

    /// <summary>"" = adb tren may nay; "ip:port" = adb server tu xa (vd laptop dang cam may).</summary>
    public string SourceAddress
    {
        get => _sourceAddress;
        set { if (Set(ref _sourceAddress, value)) { Notify(nameof(IsRemoteSource)); Notify(nameof(ConnectionLabel)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionHint)); Notify(nameof(Subtitle)); } }
    }

    /// <summary>True = may dang cam USB (serial phan cung), False = wireless (ip:port / mdns).</summary>
    public bool IsUsb
    {
        get => _isUsb;
        set { if (Set(ref _isUsb, value)) { Notify(nameof(ConnectionLabel)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionHint)); Notify(nameof(Subtitle)); } }
    }

    public bool IsRemoteSource => SourceAddress.Length > 0;

    public bool IsWireless => Serial.Contains(':') || Serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase);

    public bool IsAdbDevice => State is "device" or "unauthorized" or "offline";

    public string Title => !string.IsNullOrWhiteSpace(DeviceName)
        ? DeviceName
        : string.IsNullOrWhiteSpace(Model) ? Serial : Model;

    /// <summary>Host cua adb server tu xa, bo port 5037 mac dinh cho gon.</summary>
    public string SourceLabel
    {
        get
        {
            var address = SourceAddress;
            if (address.EndsWith(":5037", StringComparison.Ordinal)) address = address[..^5];
            return address;
        }
    }

    /// <summary>Nhan kieu ket noi hien tren badge: USB / USB <may khac> / Wireless / Wireless <may khac>.</summary>
    public string ConnectionLabel => State switch
    {
        "discovered" => "Chưa nối",
        "unauthorized" => "Chờ Allow",
        "offline" => "Offline",
        _ when IsUsb && IsRemoteSource => $"USB · {SourceLabel}",
        _ when IsUsb => "USB",
        _ when IsRemoteSource => $"Wireless · {SourceLabel}",
        _ => "Wireless",
    };

    public string ConnectionHint => State is "discovered" or "unauthorized" or "offline"
        ? "Chưa sẵn sàng"
        : IsUsb && IsRemoteSource
            ? $"Đang cắm USB vào {SourceLabel} — mirror chạy qua máy đó (không dùng wireless của điện thoại)."
            : IsUsb
                ? "Cắm USB trực tiếp vào máy tính này."
                : IsRemoteSource
                    ? $"Wireless qua adb server {SourceLabel}."
                    : "Wireless debugging của điện thoại (adb qua Wi-Fi).";

    public Brush ConnectionBrush => State is "discovered" or "unauthorized" or "offline"
        ? new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x78))
        : IsUsb
            ? new SolidColorBrush(IsRemoteSource ? Color.FromRgb(0x4C, 0x8D, 0xFF) : Color.FromRgb(0x35, 0xC4, 0x6B))
            : new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24));

    public string StateText => IsMirroring
        ? "Đang mirror"
        : State switch
        {
            "device" => "Sẵn sàng",
            "unauthorized" => "Chưa cho phép — bấm Allow trên điện thoại",
            "offline" => "Offline",
            "discovered" => "Chưa kết nối — bấm Connect",
            "pairing" => "Đang mở pairing",
            _ => State
        };

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(DeviceName) && !string.IsNullOrWhiteSpace(Model)) parts.Add(Model);
            parts.Add(Serial);
            if (!string.IsNullOrEmpty(Resolution)) parts.Add(Resolution);
            parts.Add(StateText);
            return string.Join("  •  ", parts);
        }
    }

    public Brush StatusBrush => IsMirroring
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF))
        : State switch
        {
            "device" => new SolidColorBrush(Color.FromRgb(0x35, 0xC4, 0x6B)),
            "unauthorized" => new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24)),
            "offline" => new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7)),
            "pairing" => new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24)),
            _ => new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x78))
        };

    /// <summary>Chi bat dau mirror khi adb da thay thiet bi o trang thai device.</summary>
    public bool CanStart => State == "device" && !IsMirroring;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
