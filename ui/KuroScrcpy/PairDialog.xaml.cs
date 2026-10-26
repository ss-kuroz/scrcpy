using System.Windows;
using System.Windows.Threading;
using KuroScrcpy.Services;

namespace KuroScrcpy;

public partial class PairDialog : Window
{
    private readonly AdbService _adb;
    private readonly DispatcherTimer _timer;
    private List<MdnsService> _pairing = new();

    public PairDialog(AdbService adb)
    {
        InitializeComponent();
        _adb = adb;

        ServiceList.SelectionChanged += (_, _) =>
        {
            if (ServiceList.SelectedItem is MdnsService service) AddressBox.Text = service.Address;
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { await RefreshAsync(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        var services = await _adb.GetMdnsServicesAsync();
        _pairing = services.Where(s => s.IsPairing).ToList();

        var selected = (ServiceList.SelectedItem as MdnsService)?.Address;
        ServiceList.ItemsSource = _pairing;
        if (selected != null)
        {
            ServiceList.SelectedItem = _pairing.FirstOrDefault(s => s.Address == selected);
        }
        else if (_pairing.Count == 1)
        {
            ServiceList.SelectedIndex = 0;
            AddressBox.Text = _pairing[0].Address;
        }

        if (_pairing.Count == 0)
        {
            StatusText.Text = "Chưa thấy dịch vụ pairing. Mở hộp thoại pairing trên điện thoại rồi chờ vài giây…";
        }
    }

    private async void PairBtn_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressBox.Text.Trim();
        var code = CodeBox.Text.Trim();

        if (address.Length == 0)
        {
            StatusText.Text = "Chưa có địa chỉ pairing.";
            return;
        }

        if (code.Length < 6)
        {
            StatusText.Text = "Mã pairing phải là 6 số.";
            return;
        }

        PairBtn.IsEnabled = false;
        StatusText.Text = $"Đang pair {address}…";

        try
        {
            var result = await _adb.PairAsync(address, code);
            if (result.Ok)
            {
                StatusText.Text = "Pair thành công. Đóng hộp thoại và bấm Connect.";
                CodeBox.Text = "";
                DialogResult = true;
            }
            else
            {
                StatusText.Text = "Pair lỗi: " + (result.Combined.Length > 0 ? result.Combined : "không rõ");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Pair lỗi: " + ex.Message;
        }
        finally
        {
            PairBtn.IsEnabled = true;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
