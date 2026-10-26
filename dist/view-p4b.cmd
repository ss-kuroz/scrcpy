@echo off
REM ===== realme RMX5111 : CHI XEM, cua so co dinh = res man hinh dien thoai =====
REM 2372x1080 * 1.5 = 3558x1620
cd /d "%~dp0"
start "" scrcpy.exe -s 192.168.0.152:43281 ^
  --no-control --no-audio ^
  --max-size=0 --render-fit=unscaled ^
  --window-width=3558 --window-height=1620 ^
  --window-x=140 --window-y=40 ^
  --window-title="RMX5111 view-only 2372x1080"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0lock-window.ps1"
