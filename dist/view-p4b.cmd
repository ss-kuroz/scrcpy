@echo off
REM ===== realme RMX5111 : CHI XEM, cua so khoa dung resolution man hinh =====
REM SDL3 tren Windows dung pixel vat ly -> dat dung 2372x1080, KHONG nhan he so DPI 150%.
cd /d "%~dp0"
start "" scrcpy.exe -s 192.168.0.152:43281 ^
  --no-control --audio-dup ^
  --max-size=0 --max-fps=60 --render-fit=unscaled ^
  --window-width=2372 --window-height=1080 ^
  --window-x=140 --window-y=40 ^
  --window-title="RMX5111 view-only 2372x1080"
start "" powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass ^
  -File "%~dp0lock-window.ps1" -Title "RMX5111" -Width 2372 -Height 1080 -Watch
