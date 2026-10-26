@echo off
REM ===== realme RMX5107 : CHI XEM, cua so co dinh = res man hinh dien thoai =====
REM Windows scale 150% (DPI 144) -> scrcpy chia 1.5, nen dat 2362x1080 * 1.5 = 3543x1620
REM --render-fit=unscaled : khong bao gio zoom anh
REM --no-control          : khong gui touch/phim ve may
cd /d "%~dp0"
start "" scrcpy.exe -s 192.168.0.213:39409 ^
  --no-control --no-audio ^
  --max-size=0 --render-fit=unscaled ^
  --window-width=3543 --window-height=1620 ^
  --window-x=0 --window-y=0 ^
  --window-title="RMX5107 view-only 2362x1080"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0lock-window.ps1"
