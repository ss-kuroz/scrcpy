# KuroScrcpy — UI quản lý mirror Android

WPF/.NET 9 app bọc `scrcpy.exe` trong `dist/`: tự tìm thiết bị USB + wireless, pair bằng mã 6 số,
mirror nhiều máy cùng lúc và **khoá cửa sổ đúng resolution gốc của điện thoại**.

![UI](docs/screenshot.png)

## Tải bản build

Bản build sẵn cho Windows x64 (không cần cài .NET):
**[Releases › kuro-v1.1.0](https://github.com/ss-kuroz/scrcpy/releases/tag/kuro-v1.1.0)**
→ tải `KuroScrcpy-1.1.0-win-x64.zip`, giải nén, chạy `KuroScrcpy.exe` (đã kèm sẵn `dist/`).

## Chạy từ source

```powershell
# build + chạy
dotnet run --project ui\KuroScrcpy

# hoặc build rồi mở exe
dotnet build ui\KuroScrcpy -c Release
ui\KuroScrcpy\bin\Release\net9.0-windows\KuroScrcpy.exe
```

Yêu cầu: .NET 9 Desktop Runtime (máy build bằng SDK 9.0.309), Windows 10/11.
App tự tìm `scrcpy.exe`: cạnh exe → các thư mục cha (`dist\`), hoặc biến `SCRCPY_DIR`.
Có nút **Đổi thư mục** nếu muốn trỏ tay.

## Tính năng

### Thiết bị

- Tự quét `adb devices -l` + `adb mdns services` mỗi 2.5 s.
- Gộp các transport của cùng một máy (USB / `ip:port` / mDNS) theo hardware id — không bị trùng.
- **Nhãn kiểu kết nối** ngay cạnh tên máy (rê chuột để xem tooltip):
  - `USB` (xanh lá) — cắm USB trực tiếp vào máy tính này.
  - `USB · 192.168.0.50` (xanh dương) — đang cắm vào laptop trong LAN; mirror chạy qua laptop,
    **không dùng wireless của điện thoại**.
  - `Wireless` (vàng) — wireless debugging của điện thoại tới máy này.
  - `Wireless · 192.168.0.50` — wireless qua adb server của máy khác.
- Cùng một máy xuất hiện ở nhiều nơi → chọn đường tốt nhất:
  **USB máy này → USB máy khác → Wireless máy này → Wireless máy khác**.
- **USB**: cắm dây → hiện ngay. Lần đầu máy sẽ hỏi *Allow USB debugging*, bấm Allow trên điện thoại;
  trước đó trạng thái là “Chưa cho phép”.
- **Wireless**: bật Wireless debugging + “Pair device with pairing code” trên điện thoại → bấm **Pair…**,
  chọn dịch vụ pairing (tự hiện), nhập mã 6 số. Sau khi pair, app tự `adb connect`.
- **Connect / Disconnect** cho thiết bị wireless đã pair nhưng chưa kết nối.
- **Nhớ địa chỉ wireless**: mọi `ip:port` từng kết nối được lưu lại, nên khi mDNS của adb “quên” máy
  (hay gặp sau khi điện thoại reboot / tắt bật wireless debugging) app vẫn tự `adb connect` lại đúng địa chỉ.
- **Kết nối IP**: gõ tay `ip:port` (lấy trong Wireless debugging trên máy) khi port đã đổi.
- **Tự phục hồi adb**: nếu không thấy thiết bị nào > 20 s, hoặc địa chỉ đã lưu còn mở port nhưng adb
  không nhận, app `adb kill-server` + `start-server` để quét lại mDNS (không làm khi đang mirror).
- Nút **Làm mới** để quét lại ngay.

### Chia sẻ máy này cho LAN (daemon)

Máy đang cắm điện thoại USB có thể mở adb server cho máy khác dùng: bấm **Chia sẻ máy này**
ở góc trên bên phải.

- App chạy `adb -a nodaemon server start` → listen `0.0.0.0:5037`, nút đổi thành **Đang chia sẻ ✓** (xanh).
- Thanh trạng thái hiện IP cho máy khác nhập, vd `192.168.0.183:5037`.
- Bấm lại để tắt; đóng app cũng tắt daemon.
- Lần đầu cần mở firewall (PowerShell **admin**):
  ```powershell
  New-NetFirewallRule -DisplayName "KuroScrcpy adb 5037" -Direction Inbound -Protocol TCP -LocalPort 5037 -Action Allow
  ```
- Không bật/tắt được khi đang mirror (adb server sẽ khởi động lại, làm rớt phiên).

Nếu app khởi động lại trong lúc daemon vẫn chạy, nó tự nhận ra (hỏi handshake qua IP LAN) và hiện
**Đang chia sẻ ✓** đúng trạng thái.

### Mirror qua laptop (ADB server từ xa)

Điện thoại cắm USB vào **laptop**, PC khác lấy hình qua mạng TCP/IP của laptop
(không cần bật wireless debugging trên máy).

**Trên laptop:** mở app → **Chia sẻ máy này** (hoặc chạy tay `adb -a nodaemon server start`).

**Trên PC:**

- App tự quét LAN mỗi 90 s. Máy đang mở adb server hiện thành **chip IP** ở dòng
  **ADB trong LAN:** (vd `192.168.0.50`) → bấm chip là dùng ngay, không cần biết IP.
  Nút **Quét** để quét lại lập tức; chip **máy này** (xám) nghĩa là chính máy đang chia sẻ.
- Hoặc nhập tay IP laptop vào ô **ADB laptop** (vd `192.168.0.50`) → bấm **ADB laptop**.
- App **thêm** laptop thành nguồn thứ hai (không thay adb trên máy này): danh sách gộp cả hai,
  máy cắm ở laptop có nhãn `USB · 192.168.0.50`.
- Máy nào chỉ tìm thấy ở laptop thì mirror tự chạy qua laptop
  (`ADB_SERVER_SOCKET=tcp:<ip>:5037` cho adb + `--tunnel-host=<ip>` cho scrcpy).
- Để trống rồi bấm lại = bỏ nguồn laptop.

Quét LAN chỉ quét `/24` quanh IP máy, bỏ qua adapter ảo (Hyper-V/WSL/VMware/ZeroTier) và APIPA;
nhận diện adb server bằng handshake `host:version` nên không nhầm với dịch vụ khác mở port 5037.

Dùng CLI trực tiếp cũng được:

```powershell
$env:ADB_SERVER_SOCKET = 'tcp:192.168.0.50:5037'
dist\scrcpy.exe --tunnel-host=192.168.0.50
```

Lưu ý: adb server **không mã hoá, không xác thực** — ai trong LAN cũng điều khiển được máy.
Chỉ dùng trong LAN tin cậy, hoặc dùng IP Tailscale, hoặc SSH tunnel:

```powershell
ssh -CN -L5038:localhost:5037 -L27183:localhost:27183 user@laptop
# rồi nhập 127.0.0.1:5038 vào ô ADB laptop
```

### Mirror

- Chọn thiết bị → **Bắt đầu mirror**. Mỗi máy một cửa sổ scrcpy riêng, nhiều máy chạy song song.
- **Tên cửa sổ** = tên thiết bị (`settings get global device_name`, vd `realme 15T`), fallback về model.
- **Resolution**: app đọc `cur=WxH` từ `dumpsys window displays` (đúng theo hướng đang xoay) và truyền
  `--window-width/--window-height` cho scrcpy → luôn 1:1 với màn hình điện thoại, không bị scrcpy
  scale cho vừa màn hình PC.
- **Khoá cửa sổ**: bỏ `WS_THICKFRAME` + `WS_MAXIMIZEBOX`, ép client area đúng resolution gốc,
  `--render-fit=unscaled`. Không kéo viền, không maximize, không snap được.
- **Xếp lại cửa sổ**: tự xếp lưới theo thứ tự bắt đầu (stack dọc khi 2 cửa sổ không vừa ngang).
- **Bắt đầu/Dừng/Dừng tất cả** theo từng thiết bị.

### Tuỳ chọn

| Mục | Giá trị |
| --- | --- |
| Frame rate | 30 / 60 / 90 / 120 / Max (`--max-fps`) |
| Bitrate | `8M`, `16M`, `24M`… (`--video-bit-rate`) |
| Âm thanh | **Tắt** (`--no-audio`) · **PC** (`output`, tắt loa máy) · **Cả hai** (`--audio-dup`, máy vẫn phát) |
| Xoay ngang trên PC | tự động: máy đang portrait → `--orientation=90`, client-side, **không sửa setting điện thoại** |
| Cho phép điều khiển | mặc định tắt (`--no-control` = chỉ xem) |
| Tự connect wireless | tự `adb connect` các dịch vụ mDNS tìm thấy + địa chỉ đã lưu |

## Cấu trúc

```
ui/KuroScrcpy/
  MainWindow.xaml(.cs)      UI chính: danh sách thiết bị, cấu hình, log
  PairDialog.xaml(.cs)      pair wireless bằng mã 6 số
  Models/DeviceEntry.cs     model cho danh sách thiết bị
  Services/AdbService.cs    chạy adb, parse devices/mdns, pair/connect, đọc resolution
  Services/ScrcpyRunner.cs  spawn scrcpy, theo dõi cửa sổ, khoá size, xếp cửa sổ
  Services/WindowTools.cs   Win32: EnumWindows, style, AdjustWindowRectExForDpi, SetWindowPos
  Services/AppSettings.cs   lưu %APPDATA%\KuroScrcpy\settings.json (fallback: cạnh exe)
```

## Ghi chú

- Windows SmartScreen có thể cảnh báo exe chưa ký → **More info → Run anyway**.
- App chạy scrcpy với `CreateNoWindow` nên không có cửa sổ console; log scrcpy hiện trong panel **NHẬT KÝ**.
- Cửa sổ mirror **xuất hiện ở giữa màn hình** (máy thứ 2, 3… lệch 36 px để không chồng khít).
  Bấm **Xếp lại cửa sổ** nếu muốn tile sát nhau; app không tự dời cửa sổ sau đó.
- Hai máy cùng bật “Cả hai” sẽ phát audio chung một output device của Windows.
- Resolution được chốt khi bắt đầu mirror; nếu điện thoại tự xoay giữa chừng, bấm Dừng → Bắt đầu lại.
- Wi-Fi có thể tụt frame (xem `dist/README.md`); USB ổn định hơn cho 60 fps.
