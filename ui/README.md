# KuroScrcpy — UI quản lý mirror Android

WPF/.NET 9 app bọc `scrcpy.exe` trong `dist/`: tự tìm thiết bị USB + wireless, pair bằng mã 6 số,
mirror nhiều máy cùng lúc và **khoá cửa sổ đúng resolution gốc của điện thoại**.

![UI](docs/screenshot.png)

## Chạy

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
