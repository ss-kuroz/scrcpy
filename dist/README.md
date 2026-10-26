# scrcpy v5.0.1 — Kuro build (Windows x64)

Bản build Windows của [scrcpy](https://github.com/Genymobile/scrcpy) **v5.0.1** (base commit
`a60891ae`, tag `v5.0.1`), giữ nguyên binary chính thức, thêm bộ script launcher
view-only khoá cửa sổ đúng bằng độ phân giải điện thoại.

Không patch source scrcpy — mọi thay đổi nằm ở script wrapper trong thư mục này.

## Nội dung

| File | Vai trò |
| --- | --- |
| `scrcpy.exe`, `scrcpy-server`, `*.dll` | Binary chính thức scrcpy v5.0.1 (Windows x64) |
| `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll` | ADB đi kèm release |
| `scrcpy.png`, `disconnected.png` | Icon/tray asset của release |
| `LICENSE.txt` | Apache 2.0 của scrcpy |
| `view-p4.cmd` | realme RMX5107 (2362×1080) — view-only, cửa sổ khoá cứng |
| `view-p4b.cmd` | realme RMX5111 (2372×1080) — view-only, cửa sổ khoá cứng |
| `lock-window.ps1` | Bỏ resize/maximize + ép client area đúng kích thước |
| `checklock.ps1` | In client size, DPI, trạng thái resize/maximize của cửa sổ |
| `test-lock.ps1` | Cố tình maximize/resize để kiểm tra khoá có giữ không |
| `pair-wait.cmd <code>` | Tìm dịch vụ `_adb-tls-pairing` qua mDNS rồi `adb pair` |
| `open_a_terminal_here.bat` | Mở cmd tại thư mục hiện tại |
| `scrcpy-noconsole.vbs` | Chạy scrcpy không hiện cửa sổ console |

## View-only, cửa sổ khoá đúng resolution điện thoại

`view-p4.cmd` / `view-p4b.cmd` chạy:

```bat
scrcpy.exe --no-control --max-size=0 --max-fps=60 --render-fit=unscaled ^
  --window-width=<res_ngang> --window-height=<res_doc> --window-x=0 --window-y=0 ^
  --window-title="<serial> view-only <res>"
```

rồi gọi:

```bat
lock-window.ps1 -Title RMX5107 -Width 2362 -Height 1080 -Watch
```

- `--no-control`: không gửi touch/phím về máy → chỉ xem.
- `--render-fit=unscaled`: không bao giờ zoom ảnh, 1 pixel điện thoại = 1 pixel màn hình.
- `--max-size=0`: lấy đúng độ phân giải gốc của máy.
- `--max-fps=60`: cap capture ở 60 fps (máy có thể tự nhảy 90/120/144 Hz, không cap thì
  scrcpy bám theo và tốn Wi-Fi/CPU vô ích).

### Audio

Script **không** truyền `--no-audio`, nên scrcpy forward cả tiếng. Mặc định
`--audio-source=auto` → với display mirroring sẽ chọn `output`:

| Nguồn | Hành vi |
| --- | --- |
| `output` (mặc định) | Forward toàn bộ âm thanh ra **PC**, đồng thời **tắt loa điện thoại** |
| `playback` | Chỉ lấy audio của app đang play (app có thể opt-out), điện thoại vẫn phát |
| `playback` + `--audio-dup` | **Vừa phát trên PC vừa phát trên loa điện thoại** (script `view-p4*.cmd` dùng cách này) |
| `mic` | Lấy micro |

- Hai điện thoại cùng mở → hai luồng audio **mix chung** vào output device mặc định của
  Windows (scrcpy không cho chọn thiết bị ra). Muốn tách thì đổi default output hoặc
  dùng virtual cable.
- Muốn điện thoại vẫn kêu: thêm `--audio-dup`.
- Muốn tắt tiếng hoàn toàn: thêm `--no-audio`.
- Audio qua Wi-Fi dễ rè/giật do jitter; USB ổn định hơn. Nếu nghe giật, tăng
  `--audio-buffer=200`.

### FPS thực tế

Đo trên RMX5107 (display đang ở mode 60 Hz, `--print-fps`):

- Cao nhất 60–61 fps, nhưng trung bình ~52 fps khi vuốt liên tục, có lúc tụt 30–40 fps
  và thỉnh thoảng `+1 frames skipped`.
- Nguyên nhân: đang mirror **qua Wi-Fi**; màn hình tĩnh thì encoder cũng gần như không
  gửi frame mới nên số đo thấp, còn lúc có chuyển động thì phụ thuộc băng thông Wi-Fi.
- Muốn 60 fps ổn định: cắm **USB** (`-s <serial>` không dùng `ip:port`) hoặc giảm
  `--max-size`/`--video-bit-rate`.

### Vì sao không cần nhân 1.5 cho DPI

Trên Windows, SDL3 dùng **pixel vật lý** cho kích thước cửa sổ: tạo window 2362×1080
thì `GetClientRect` cũng là 2362×1080, kể cả khi Windows đang scale 150% (DPI 144).
`SDL_GetWindowPixelDensity` trả về 1.0, `SDL_GetWindowDisplayScale` mới là 1.5.

Cái bẫy là **PowerShell 5.1 là DPI-unaware**: nếu không bật DPI awareness, `GetClientRect`
bị Windows ảo hoá và báo 1574×720 (= 2362 ÷ 1.5). Vì vậy cả `lock-window.ps1` và
`checklock.ps1` đều gọi `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` trước khi
đọc/ghi kích thước — nếu không sẽ tưởng cửa sổ nhỏ hơn thực tế và đặt sai.

### `lock-window.ps1` làm gì

| Tham số | Ý nghĩa |
| --- | --- |
| `-Title` | Phần title để nhận diện cửa sổ (mặc định `*` = mọi cửa sổ scrcpy) |
| `-Width` / `-Height` | Client area mong muốn, tính bằng pixel vật lý (0 = không đổi size) |
| `-ProcessName` | Tên process, mặc định `scrcpy` |
| `-Timeout` | Số giây chờ cửa sổ xuất hiện (mặc định 25) |
| `-NoMinimize` | Bỏ luôn nút minimize |
| `-Watch` | Bám cửa sổ đến khi scrcpy thoát, tự ép lại nếu bị đổi size/style |

Cơ chế:

1. Bỏ `WS_THICKFRAME` → không kéo viền, không Aero Snap.
2. Bỏ `WS_MAXIMIZEBOX` → double-click titlebar, `Win+↑`, menu system đều không maximize được.
3. Ép client area = `-Width`×`-Height` bằng `AdjustWindowRectExForDpi`, đo lại và bù sai số
   tối đa 3 vòng (frame metrics đổi khi bỏ `WS_THICKFRAME`).
4. Neo lại góc trên-trái của client để video không bị lệch khi frame đổi.
5. Bỏ qua cửa sổ console của scrcpy (cùng title nhưng class `ConsoleWindowClass`).

`-Watch` poll mỗi 300 ms; nếu ai đó/cái gì đó resize cửa sổ, nó kéo về đúng size trong
~0.3–0.6 s. Khi scrcpy thoát, watcher tự kết thúc.

### Kiểm tra

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File checklock.ps1
```

```
RMX5107 view-only 2362x1080 | pid 40688 | client 2362x1080 | window 2368x1120 @ (-3,-37)
                            | dpi 144 | resize=False maximize=False minimize=True
```

`client` phải đúng bằng resolution điện thoại, `resize`/`maximize` phải `False`.

Thử phá khoá (watcher phải kéo về đúng size trong ~1 s):

```bat
powershell -NoProfile -File test-lock.ps1 -Action maximize
powershell -NoProfile -File test-lock.ps1 -Action resize -Width 1200 -Height 700
```

## Điều kiện để video khớp 1:1

- Điện thoại phải đang ở **landscape** (2362×1080), nếu không scrcpy nhận frame dọc
  1080×2362 và cửa sổ ngang sẽ cắt/letterbox ảnh. Khoá rotation bằng adb:

  ```bat
  adb shell settings put system accelerometer_rotation 0
  adb shell settings put system user_rotation 1
  ```

  Trả lại tự động xoay: `settings put system accelerometer_rotation 1`.
- `scrcpy.exe` là app console nên `start "" scrcpy.exe` để lại một cửa sổ console
  (title `scrcpy - ...`). `lock-window.ps1` bỏ qua cửa sổ này; muốn không có console
  thì dùng `scrcpy-noconsole.vbs`.
- Cửa sổ được đặt theo góc client (`--window-x/--window-y` là góc client, không phải góc
  window), nên với `0,0` titlebar nằm ngoài màn hình; kéo cửa sổ nếu cần thấy titlebar.

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
