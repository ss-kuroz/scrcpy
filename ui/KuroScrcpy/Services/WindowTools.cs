using System.Runtime.InteropServices;
using System.Text;

namespace KuroScrcpy.Services;

/// <summary>Win32 helper: tim cua so SDL cua scrcpy, khoa resize/maximize, ep dung kich thuoc pixel.</summary>
public static class WindowTools
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_MAXIMIZEBOX = 0x00010000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);

    public static long GetStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64() : GetWindowLong32(hWnd, GWL_STYLE);

    private static long GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hWnd, GWL_EXSTYLE);

    private static void SetStyle(IntPtr hWnd, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(hWnd, GWL_STYLE, new IntPtr(value));
        else SetWindowLong32(hWnd, GWL_STYLE, (int)value);
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Tim cua so SDL (video) cua mot process scrcpy.</summary>
    public static List<IntPtr> FindScrcpyWindows(int processId)
    {
        var result = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != (uint)processId) return true;
            if (!IsWindowVisible(hWnd)) return true;
            if (GetClassName(hWnd) != "SDL_app") return true;
            result.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static (int Width, int Height) GetClientSize(IntPtr hWnd)
    {
        GetClientRect(hWnd, out var rect);
        return (rect.Width, rect.Height);
    }

    public static (int X, int Y) GetClientOrigin(IntPtr hWnd)
    {
        var point = new POINT { X = 0, Y = 0 };
        ClientToScreen(hWnd, ref point);
        return (point.X, point.Y);
    }

    public static RECT GetWorkArea()
    {
        var monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)) return info.rcWork;
        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    private static (int Width, int Height) GetFrameSize(int clientW, int clientH, long style, long exStyle, uint dpi)
    {
        var rect = new RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
        bool ok;
        try
        {
            ok = AdjustWindowRectExForDpi(ref rect, (uint)style, false, (uint)exStyle, dpi == 0 ? 96u : dpi);
        }
        catch (EntryPointNotFoundException)
        {
            ok = AdjustWindowRectEx(ref rect, (uint)style, false, (uint)exStyle);
        }

        return ok ? (rect.Width, rect.Height) : (clientW, clientH);
    }

    /// <summary>
    /// Bo WS_THICKFRAME/WS_MAXIMIZEBOX va ep client area dung WxH pixel.
    /// Kich thuoc duoc do lai va bu sai so toi da 3 vong.
    /// </summary>
    public static void LockAndResize(IntPtr hWnd, int targetWidth, int targetHeight)
    {
        if (hWnd == IntPtr.Zero) return;

        if (IsZoomed(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
            Thread.Sleep(80);
        }

        var origin = GetClientOrigin(hWnd);
        var style = GetStyle(hWnd);
        var newStyle = style & ~(WS_THICKFRAME | WS_MAXIMIZEBOX);
        var styleChanged = newStyle != style;
        if (styleChanged) SetStyle(hWnd, newStyle);

        var (clientW, clientH) = GetClientSize(hWnd);
        if (targetWidth <= 0 || targetHeight <= 0) return;

        if (styleChanged || clientW != targetWidth || clientH != targetHeight)
        {
            var dpi = GetDpiForWindow(hWnd);
            var (outerW, outerH) = GetFrameSize(targetWidth, targetHeight, newStyle, GetExStyle(hWnd), dpi);
            GetWindowRect(hWnd, out var windowRect);

            for (var i = 0; i < 3; i++)
            {
                SetWindowPos(hWnd, IntPtr.Zero, windowRect.Left, windowRect.Top, outerW, outerH,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                Thread.Sleep(50);

                (clientW, clientH) = GetClientSize(hWnd);
                if (clientW == targetWidth && clientH == targetHeight) break;

                outerW += targetWidth - clientW;
                outerH += targetHeight - clientH;
            }
        }

        var now = GetClientOrigin(hWnd);
        if (now.X != origin.X || now.Y != origin.Y)
        {
            GetWindowRect(hWnd, out var rect);
            SetWindowPos(hWnd, IntPtr.Zero, rect.Left + (origin.X - now.X), rect.Top + (origin.Y - now.Y),
                         0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>Di chuyen cua so sao cho goc tren-trai cua client nam dung (x, y) pixel man hinh.</summary>
    public static void MoveClientTo(IntPtr hWnd, int x, int y)
    {
        if (hWnd == IntPtr.Zero) return;
        var origin = GetClientOrigin(hWnd);
        if (origin.X == x && origin.Y == y) return;

        GetWindowRect(hWnd, out var rect);
        SetWindowPos(hWnd, IntPtr.Zero, rect.Left + (x - origin.X), rect.Top + (y - origin.Y),
                     0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static bool IsLocked(IntPtr hWnd)
    {
        var style = GetStyle(hWnd);
        return (style & WS_THICKFRAME) == 0 && (style & WS_MAXIMIZEBOX) == 0;
    }
}
