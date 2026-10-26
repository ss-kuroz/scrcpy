Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
Get-Process scrcpy -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object {
  $r = New-Object W+RECT
  [void][W]::GetWindowRect($_.MainWindowHandle, [ref]$r)
  "{0} | window {1}x{2} @ ({3},{4})" -f $_.MainWindowTitle, ($r.R-$r.L), ($r.B-$r.T), $r.L, $r.T
}
