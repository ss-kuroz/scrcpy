# Thu co tinh pha khoa: maximize / resize cua so scrcpy de kiem tra lock-window.ps1.
# Vi du:
#   powershell -NoProfile -File test-lock.ps1 -Action maximize
#   powershell -NoProfile -File test-lock.ps1 -Action resize -Width 1200 -Height 700
#   powershell -NoProfile -File test-lock.ps1 -Action info
param(
  [ValidateSet("info", "maximize", "resize", "restore")]
  [string]$Action = "info",
  [string]$Title = "view-only",
  [int]$Width = 1600,
  [int]$Height = 900
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class LockTest {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after,
      int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);

  public static void DpiAware() {
    try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
    try { SetProcessDpiAwareness(2); } catch { }
  }

  public static IntPtr Find(string titlePart) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      StringBuilder cls = new StringBuilder(256);
      GetClassName(h, cls, 256);
      if (cls.ToString() != "SDL_app") return true;
      int len = GetWindowTextLength(h);
      if (len <= 0) return true;
      StringBuilder t = new StringBuilder(len + 2);
      GetWindowText(h, t, t.Capacity);
      if (t.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0) {
        found = h;
        return false;
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  public static string Info(IntPtr h) {
    RECT c;
    GetClientRect(h, out c);
    return string.Format("hwnd 0x{0:X} | client {1}x{2} | zoomed {3}",
                         h.ToInt64(), c.Right - c.Left, c.Bottom - c.Top, IsZoomed(h));
  }
}
"@

[LockTest]::DpiAware()

$h = [LockTest]::Find($Title)
if ($h -eq [IntPtr]::Zero) {
  Write-Output "khong thay cua so nao chua '$Title'"
  exit 1
}

switch ($Action) {
  "maximize" { [void][LockTest]::ShowWindow($h, 3); Start-Sleep -Milliseconds 500 }  # SW_MAXIMIZE
  "restore"  { [void][LockTest]::ShowWindow($h, 9); Start-Sleep -Milliseconds 500 }  # SW_RESTORE
  "resize"   { [void][LockTest]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x0004) }
}

# Cho watcher bat duoc thay doi (neu -Watch dang chay)
if ($Action -ne "info") { Start-Sleep -Milliseconds 1200 }

Write-Output ([LockTest]::Info($h))
