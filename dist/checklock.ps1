Add-Type @"
using System; using System.Runtime.InteropServices;
public class CK {
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
$T=0x00040000; $M=0x00010000
foreach ($p in @(Get-Process scrcpy -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
  $s=[CK]::GetWindowLong($p.MainWindowHandle,-16)
  $c=New-Object CK+RECT; [void][CK]::GetClientRect($p.MainWindowHandle,[ref]$c)
  "{0} | client {1}x{2} | resize={3} maximize={4}" -f $p.MainWindowTitle, ($c.R-$c.L), ($c.B-$c.T), [bool]($s -band $T), [bool]($s -band $M)
}
