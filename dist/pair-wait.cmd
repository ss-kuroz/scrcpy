@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (echo Usage: pair-wait.cmd PAIRING_CODE & exit /b 1)
set CODE=%~1
set FOUND=
for /L %%i in (1,1,40) do (
  for /f "tokens=1,2,3" %%a in ('adb.exe mdns services ^| findstr "_adb-tls-pairing"') do (
    set FOUND=%%c
    goto :got
  )
  timeout /t 2 /nobreak >nul
)
echo [!] Khong thay dich vu pairing. Mo dialog "Pair device with pairing code" tren dien thoai roi chay lai.
exit /b 1
:got
echo [+] Pairing service: %FOUND%
adb.exe pair %FOUND% %CODE%
