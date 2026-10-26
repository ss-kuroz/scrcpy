@echo off
REM ===== realme RMX5107 : CHI XEM, cua so khoa dung resolution man hinh =====
REM SDL3 tren Windows dung pixel vat ly -> dat dung 2362x1080, KHONG nhan he so DPI 150%.
REM --render-fit=unscaled : khong bao gio zoom anh
REM --no-control          : khong gui touch/phim ve may
REM lock-window.ps1 -Watch: bo vien resize + maximize, ep client = 2362x1080
cd /d "%~dp0"
start "" scrcpy.exe -s 192.168.0.213:39409 ^
  --no-control --no-audio ^
  --max-size=0 --render-fit=unscaled ^
  --window-width=2362 --window-height=1080 ^
  --window-x=0 --window-y=0 ^
  --window-title="RMX5107 view-only 2362x1080"
start "" powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass ^
  -File "%~dp0lock-window.ps1" -Title "RMX5107" -Width 2362 -Height 1080 -Watch
