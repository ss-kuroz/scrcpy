# Khoa cua so scrcpy dung kich thuoc man hinh dien thoai.
#
# - Bo WS_THICKFRAME  -> khong keo vien / Aero Snap duoc
# - Bo WS_MAXIMIZEBOX -> khong maximize duoc (double-click titlebar, Win+Up, menu deu tat)
# - Dat client area = Width x Height pixel vat ly. SDL3 tren Windows dung pixel that,
#   KHONG nhan he so DPI 150% -> video 1:1, khong bi scale
# - -Watch: bam cua so den khi scrcpy thoat; neu size/style bi doi thi ep lai
#
# Vi du:
#   powershell -NoProfile -File lock-window.ps1 -Title RMX5107 -Width 2362 -Height 1080 -Watch
param(
  [string]$Title = "*",
  [int]$Width = 0,
  [int]$Height = 0,
  [string]$ProcessName = "scrcpy",
  [int]$Timeout = 25,
  [switch]$NoMinimize,
  [switch]$Watch
)

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinLock {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }

  [StructLayout(LayoutKind.Sequential)]
  public struct POINT { public int X, Y; }

  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);

  // PowerShell 5.1 la DPI-unaware: neu khong bat cai nay, GetClientRect tra ve
  // kich thuoc "ao" (chia 1.5 tren man hinh 150%) thay vi pixel vat ly.
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);
  public static void MakeDpiAware() {
    try {
      if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; // PER_MONITOR_AWARE_V2
    } catch (EntryPointNotFoundException) { }
    catch (DllNotFoundException) { }
    try { SetProcessDpiAwareness(2); } catch { }               // PROCESS_PER_MONITOR_DPI_AWARE
  }

  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetClassName(IntPtr h, StringBuilder s, int max);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
  static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
  static extern int GetWindowLong32(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
  static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
  static extern int SetWindowLong32(IntPtr h, int i, int v);

  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool SetWindowPos(IntPtr h, IntPtr insertAfter,
                                         int x, int y, int cx, int cy, uint flags);

  [DllImport("user32.dll")]
  static extern bool AdjustWindowRectExForDpi(ref RECT r, uint style, bool menu,
                                              uint exStyle, uint dpi);
  [DllImport("user32.dll")]
  static extern bool AdjustWindowRectEx(ref RECT r, uint style, bool menu,
                                        uint exStyle);

  const int GWL_STYLE = -16;
  const int GWL_EXSTYLE = -20;

  public static string GetTitle(IntPtr h) {
    int len = GetWindowTextLength(h);
    if (len <= 0) return "";
    StringBuilder sb = new StringBuilder(len + 2);
    GetWindowText(h, sb, sb.Capacity);
    return sb.ToString();
  }

  public static string GetClass(IntPtr h) {
    StringBuilder sb = new StringBuilder(256);
    GetClassName(h, sb, sb.Capacity);
    return sb.ToString();
  }

  // Liet ke cua so top-level cua process (pid > 0) co title chua titlePart.
  public static IntPtr[] FindWindows(int pid, string titlePart, bool visibleOnly) {
    List<IntPtr> found = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint wpid;
      GetWindowThreadProcessId(h, out wpid);
      if (pid > 0 && wpid != (uint) pid) return true;
      if (visibleOnly && !IsWindowVisible(h)) return true;
      string cls = GetClass(h);
      // Bo qua console cua scrcpy (cung title) va cac cua so IME
      if (cls == "ConsoleWindowClass" || cls == "IME" || cls == "MSCTFIME UI") return true;
      if (titlePart != null && titlePart != "" && titlePart != "*") {
        string t = GetTitle(h);
        if (t.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0) return true;
      }
      found.Add(h);
      return true;
    }, IntPtr.Zero);
    return found.ToArray();
  }

  public static long GetStyle(IntPtr h) {
    return IntPtr.Size == 8 ? GetWindowLongPtr(h, GWL_STYLE).ToInt64()
                            : GetWindowLong32(h, GWL_STYLE);
  }

  public static long GetExStyle(IntPtr h) {
    return IntPtr.Size == 8 ? GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64()
                            : GetWindowLong32(h, GWL_EXSTYLE);
  }

  public static void SetStyle(IntPtr h, long value) {
    if (IntPtr.Size == 8) {
      SetWindowLongPtr(h, GWL_STYLE, new IntPtr(value));
    } else {
      SetWindowLong32(h, GWL_STYLE, (int) value);
    }
  }

  // Kich thuoc cua so ngoai (titlebar + border) de client area dung clientW x clientH
  public static int[] FrameSize(int clientW, int clientH, long style,
                                long exStyle, uint dpi) {
    RECT r = new RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
    bool ok;
    try {
      ok = AdjustWindowRectExForDpi(ref r, (uint) style, false, (uint) exStyle,
                                    dpi == 0 ? 96u : dpi);
    } catch (EntryPointNotFoundException) {
      ok = AdjustWindowRectEx(ref r, (uint) style, false, (uint) exStyle);
    }
    if (!ok) return new int[] { clientW, clientH };
    return new int[] { r.Right - r.Left, r.Bottom - r.Top };
  }
}
"@

[WinLock]::MakeDpiAware()

$WS_THICKFRAME    = 0x00040000
$WS_MAXIMIZEBOX   = 0x00010000
$WS_MINIMIZEBOX   = 0x00020000
$SW_RESTORE       = 9
$SWP_NOSIZE       = 0x0001
$SWP_NOMOVE       = 0x0002
$SWP_NOZORDER     = 0x0004
$SWP_NOACTIVATE   = 0x0010
$SWP_FRAMECHANGED = 0x0020

function Get-TargetWindow {
  foreach ($proc in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
    foreach ($h in [WinLock]::FindWindows($proc.Id, $Title, $true)) {
      [pscustomobject]@{
        Handle  = $h
        Title   = [WinLock]::GetTitle($h)
        Process = $proc
      }
    }
  }
}

function Lock-Window {
  param([IntPtr]$Handle)

  # Dang maximize thi restore truoc, neu khong Windows se chan viec dat size
  if ([WinLock]::IsZoomed($Handle)) {
    [void][WinLock]::ShowWindow($Handle, $SW_RESTORE)
    Start-Sleep -Milliseconds 120
  }

  # Ghi lai goc tren-trai client truoc khi doi frame, de video khong bi lech cho
  $origin = New-Object WinLock+POINT
  [void][WinLock]::ClientToScreen($Handle, [ref]$origin)

  $style = [WinLock]::GetStyle($Handle)
  $blocked = [int64]($WS_THICKFRAME -bor $WS_MAXIMIZEBOX)
  if ($NoMinimize) { $blocked = $blocked -bor $WS_MINIMIZEBOX }
  $newStyle = $style -band (-bnot $blocked)
  $styleChanged = $newStyle -ne $style
  if ($styleChanged) { [WinLock]::SetStyle($Handle, $newStyle) }

  if ($Width -gt 0 -and $Height -gt 0) {
    $client = New-Object WinLock+RECT
    [void][WinLock]::GetClientRect($Handle, [ref]$client)
    $clientW = $client.Right - $client.Left
    $clientH = $client.Bottom - $client.Top

    if ($styleChanged -or $clientW -ne $Width -or $clientH -ne $Height) {
      $dpi = [WinLock]::GetDpiForWindow($Handle)
      $frame = [WinLock]::FrameSize($Width, $Height, $newStyle,
                                    [WinLock]::GetExStyle($Handle), $dpi)
      $window = New-Object WinLock+RECT
      [void][WinLock]::GetWindowRect($Handle, [ref]$window)

      # Frame metrics cua Windows khong luon khop (nhat la sau khi bo WS_THICKFRAME),
      # nen dat size roi do lai, bu sai so toi da 3 lan.
      $outerW = $frame[0]
      $outerH = $frame[1]
      for ($i = 0; $i -lt 3; $i++) {
        [void][WinLock]::SetWindowPos($Handle, [IntPtr]::Zero, $window.Left, $window.Top,
                                      $outerW, $outerH,
                                      $SWP_NOZORDER -bor $SWP_NOACTIVATE -bor $SWP_FRAMECHANGED)
        Start-Sleep -Milliseconds 60

        $probe = New-Object WinLock+RECT
        [void][WinLock]::GetClientRect($Handle, [ref]$probe)
        $cw = $probe.Right - $probe.Left
        $ch = $probe.Bottom - $probe.Top
        if ($cw -eq $Width -and $ch -eq $Height) { break }

        $outerW += $Width - $cw
        $outerH += $Height - $ch
      }
    }
  } elseif ($styleChanged) {
    [void][WinLock]::SetWindowPos($Handle, [IntPtr]::Zero, 0, 0, 0, 0,
                                  $SWP_NOMOVE -bor $SWP_NOSIZE -bor $SWP_NOZORDER -bor
                                  $SWP_NOACTIVATE -bor $SWP_FRAMECHANGED)
  }

  # Doi frame lam client dich vai pixel -> keo cua so ve lai dung vi tri cu
  $now = New-Object WinLock+POINT
  [void][WinLock]::ClientToScreen($Handle, [ref]$now)
  $dx = $origin.X - $now.X
  $dy = $origin.Y - $now.Y
  if ($dx -ne 0 -or $dy -ne 0) {
    $wr = New-Object WinLock+RECT
    [void][WinLock]::GetWindowRect($Handle, [ref]$wr)
    [void][WinLock]::SetWindowPos($Handle, [IntPtr]::Zero, $wr.Left + $dx, $wr.Top + $dy, 0, 0,
                                  $SWP_NOSIZE -bor $SWP_NOZORDER -bor $SWP_NOACTIVATE)
  }

  $final = New-Object WinLock+RECT
  [void][WinLock]::GetClientRect($Handle, [ref]$final)
  $finalW = $final.Right - $final.Left
  $finalH = $final.Bottom - $final.Top
  $finalStyle = [WinLock]::GetStyle($Handle)

  [pscustomobject]@{
    ClientW  = $finalW
    ClientH  = $finalH
    Dpi      = [WinLock]::GetDpiForWindow($Handle)
    Resize   = [bool]($finalStyle -band $WS_THICKFRAME)
    Maximize = [bool]($finalStyle -band $WS_MAXIMIZEBOX)
    Minimize = [bool]($finalStyle -band $WS_MINIMIZEBOX)
    Ok       = ($Width -le 0 -and $Height -le 0) -or
               ($finalW -eq $Width -and $finalH -eq $Height)
  }
}

$startTime = Get-Date
$lastState = @{}
$idle = 0

while ($true) {
  $targets = @(Get-TargetWindow)

  if ($targets.Count -eq 0) {
    $idle++
  } else {
    $idle = 0
    foreach ($t in $targets) {
      $r = Lock-Window -Handle $t.Handle
      $key = "{0}x{1}|{2}|{3}|{4}|{5}" -f $r.ClientW, $r.ClientH, $r.Resize,
             $r.Maximize, $r.Minimize, $r.Dpi
      if (-not $lastState.ContainsKey($t.Handle) -or $lastState[$t.Handle] -ne $key) {
        $lastState[$t.Handle] = $key
        if ($r.Ok) {
          Write-Output ("LOCKED {0} | client {1}x{2} | dpi {3} | resize={4} maximize={5} minimize={6}" -f `
            $t.Title, $r.ClientW, $r.ClientH, $r.Dpi, $r.Resize, $r.Maximize, $r.Minimize)
        } else {
          Write-Output ("WARN {0} | client {1}x{2} (muon {3}x{4})" -f `
            $t.Title, $r.ClientW, $r.ClientH, $Width, $Height)
        }
      }
    }
  }

  $elapsed = ((Get-Date) - $startTime).TotalSeconds
  if ($Watch) {
    if ($lastState.Count -gt 0) {
      # Da khoa duoc cua so: scrcpy thoat (khong con cua so ~3s) thi ket thuc
      if ($idle -gt 10) { break }
    } elseif ($elapsed -gt $Timeout) {
      # Chua bao gio thay cua so trong thoi gian cho -> ket thuc
      break
    }
  } else {
    if ($lastState.Count -gt 0) {
      # Khoa xong, cho on dinh ~1.5s roi thoat
      if ($idle -gt 5) { break }
    } elseif ($elapsed -gt $Timeout) {
      break
    }
  }

  Start-Sleep -Milliseconds 300
}

Write-Output ("done: {0} cua so da khoa" -f $lastState.Count)
