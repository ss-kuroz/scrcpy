# In trang thai cua so scrcpy: client size, DPI, cac co resize/maximize.
# Dung de kiem tra sau khi chay view-p4.cmd / view-p4b.cmd.
param(
  [string]$ProcessName = "scrcpy",
  [string]$Title = "*"
)

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CK {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }

  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  // PowerShell 5.1 la DPI-unaware -> phai bat de doc pixel vat ly, khong bi chia 1.5
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);
  public static void MakeDpiAware() {
    try {
      if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
    } catch (EntryPointNotFoundException) { }
    catch (DllNotFoundException) { }
    try { SetProcessDpiAwareness(2); } catch { }
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

  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);

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

  public static IntPtr[] FindWindows(int pid, string titlePart) {
    List<IntPtr> found = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint wpid;
      GetWindowThreadProcessId(h, out wpid);
      if (pid > 0 && wpid != (uint) pid) return true;
      if (!IsWindowVisible(h)) return true;
      string cls = GetClass(h);
      if (cls == "ConsoleWindowClass" || cls == "IME" || cls == "MSCTFIME UI") return true;
      if (titlePart != null && titlePart != "" && titlePart != "*") {
        if (GetTitle(h).IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0) return true;
      }
      found.Add(h);
      return true;
    }, IntPtr.Zero);
    return found.ToArray();
  }

  public static long Style(IntPtr h) {
    return IntPtr.Size == 8 ? GetWindowLongPtr(h, -16).ToInt64() : GetWindowLong32(h, -16);
  }
}
"@

[CK]::MakeDpiAware()

$THICK = 0x00040000
$MAXB  = 0x00010000
$MINB  = 0x00020000

$found = 0
foreach ($p in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
  foreach ($h in [CK]::FindWindows($p.Id, $Title)) {
    $found++
    $c = New-Object CK+RECT; [void][CK]::GetClientRect($h, [ref]$c)
    $w = New-Object CK+RECT; [void][CK]::GetWindowRect($h, [ref]$w)
    $s = [CK]::Style($h)
    "{0} | pid {1} | client {2}x{3} | window {4}x{5} @ ({6},{7}) | dpi {8} | resize={9} maximize={10} minimize={11}" -f `
      [CK]::GetTitle($h), $p.Id,
      ($c.Right - $c.Left), ($c.Bottom - $c.Top),
      ($w.Right - $w.Left), ($w.Bottom - $w.Top), $w.Left, $w.Top,
      [CK]::GetDpiForWindow($h),
      [bool]($s -band $THICK), [bool]($s -band $MAXB), [bool]($s -band $MINB)
  }
}

if ($found -eq 0) {
  Write-Output "khong tim thay cua so cua $ProcessName"
}
