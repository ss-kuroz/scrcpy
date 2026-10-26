# scrcpy v5.0.1 — Kuro build (Windows x64)

Bản build Windows của [scrcpy](https://github.com/Genymobile/scrcpy) **v5.0.1** (base commit
`a60891ae`, tag `v5.0.1`), giữ nguyên binary chính thức, thêm bộ script launcher
view-only và khoá cửa sổ dùng cho phòng train.

Không patch source scrcpy — mọi thay đổi nằm ở script wrapper trong thư mục này.

## Nội dung

| File | Vai trò |
| --- | --- |
| `scrcpy.exe`, `scrcpy-server`, `*.dll` | Binary chính thức scrcpy v5.0.1 (Windows x64) |
| `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll` | ADB đi kèm release |
| `scrcpy.png`, `disconnected.png` | Icon/tray asset của release |
| `LICENSE.txt` | Apache 2.0 của scrcpy |
| `view-p4.cmd` | realme RMX5107 — view-only, cửa sổ cố định 3543×1620 @ (0,0) |
| `view-p4b.cmd` | realme RMX5111 — view-only, cửa sổ cố định 3558×1620 @ (140,40) |
| `lock-window.ps1` | Bỏ `WS_THICKFRAME` + `WS_MAXIMIZEBOX` → không kéo viền, không maximize |
| `checklock.ps1` | In client size + cờ resize/maximize của cửa sổ scrcpy |
| `winsize.ps1` | In window rect của cửa sổ scrcpy |
| `pair-wait.cmd <code>` | Tìm dịch vụ `_adb-tls-pairing` qua mDNS rồi `adb pair` |
| `open_a_terminal_here.bat` | Mở cmd tại thư mục hiện tại |
| `scrcpy-noconsole.vbs` | Chạy scrcpy không hiện cửa sổ console |

## Script view-only

`view-p4.cmd` / `view-p4b.cmd` chạy:

```bat
scrcpy.exe --no-control --no-audio --max-size=0 --render-fit=unscaled ^
  --window-width=<w> --window-height=<h> --window-x=<x> --window-y=<y> ^
  --window-title="<serial> view-only <res>"
```

rồi gọi `lock-window.ps1`.

- `--no-control`: không gửi touch/phím về máy → chỉ xem.
- `--render-fit=unscaled`: không bao giờ zoom ảnh, 1 pixel điện thoại = N pixel cửa sổ.
- **Hệ số 1.5**: Windows đang scale 150% (DPI 144), scrcpy chia kích thước cho 1.5,
  nên phải đặt `window-width = res_ngang × 1.5`, `window-height = 1080 × 1.5 = 1620`
  để cửa sổ khớp đúng độ phân giải màn hình điện thoại.
- Đổi máy khác thì sửa `-s <ip:port>`, kích thước và vị trí trong file `.cmd`.

`lock-window.ps1` poll 25 giây để bắt cửa sổ vừa mở, chỉ sửa window style, **không**
đổi kích thước/vị trí (đổi size sẽ khiến scrcpy tự tính lại và nhảy cửa sổ).

## Pair không dây

```bat
pair-wait.cmd 123456
```

Mở "Pair device with pairing code" trên điện thoại trước, script sẽ tự tìm
`_adb-tls-pairing` (40 lần × 2 giây) và gọi `adb pair`.

## Ghi chú

- Bundle này là snapshot release, không build lại từ source; muốn build từ source
  xem hướng dẫn trong `doc/` ở nhánh `master`.
- Nhánh `master` của fork giữ nguyên upstream; mọi thay đổi của bản Kuro nằm ở
  nhánh `kuro-5.0.1`.
