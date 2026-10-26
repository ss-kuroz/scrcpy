# Khoa cung cua so scrcpy: bo WS_THICKFRAME (khong keo vien duoc) + WS_MAXIMIZEBOX (khong phong to duoc).
# Khong doi kich thuoc/vi tri -> khong lam scrcpy tu chinh lai.
Add-Type @"
using System; using System.Runtime.InteropServices;
public class LK5 {
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
$THICK = 0x00040000; $MAXB = 0x00010000; $GWLS = -16
$locked = @{}
$idle = 0
$deadline = (Get-Date).AddSeconds(25)
while ((Get-Date) -lt $deadline) {
  foreach ($proc in @(Get-Process scrcpy -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
    $hwnd = $proc.MainWindowHandle
    $styleNow = [LK5]::GetWindowLong($hwnd, $GWLS)
    $styleLocked = ($styleNow -band (-bnot $THICK)) -band (-bnot $MAXB)
    if ($styleLocked -ne $styleNow) { [void][LK5]::SetWindowLong($hwnd, $GWLS, $styleLocked); $idle = 0 }
    if (-not $locked.ContainsKey($hwnd)) {
      $cli = New-Object LK5+RECT; [void][LK5]::GetClientRect($hwnd, [ref]$cli)
      Write-Output ("LOCKED " + $proc.MainWindowTitle + "  client=" + ($cli.R-$cli.L) + "x" + ($cli.B-$cli.T))
      $locked[$hwnd] = 1
      $idle = 0
    }
  }
  $idle++
  if ($locked.Count -gt 0 -and $idle -gt 6) { break }
  Start-Sleep -Milliseconds 300
}
Write-Output ("done: " + $locked.Count + " cua so da khoa resize")
