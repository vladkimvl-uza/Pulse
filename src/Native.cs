using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse
{
    static class Native
    {
        public const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000,
                         WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1), HWND_TOPMOST = new IntPtr(-1),
                                      HWND_NOTOPMOST = new IntPtr(-2), HWND_TOP = IntPtr.Zero, HWND_MESSAGE = new IntPtr(-3);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10,
                          SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
        public const int WM_WINDOWPOSCHANGING = 0x46, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3,
                         WM_DPICHANGED = 0x2E0, WM_HOTKEY = 0x312, WM_DISPLAYCHANGE = 0x7E;

        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct DWM_BLURBEHIND { public int dwFlags; public bool fEnable; public IntPtr hRgnBlur; public bool fTransitionOnMaximized; }

        [DllImport("dwmapi.dll")] public static extern int DwmEnableBlurBehindWindow(IntPtr h, ref DWM_BLURBEHIND b);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        /// <summary>Seconds since the last keyboard or mouse input in this session.</summary>
        public static double IdleSeconds() { var i = new LASTINPUTINFO { cbSize = 8 }; return GetLastInputInfo(ref i) ? unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0 : 0; }
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [StructLayout(LayoutKind.Sequential)] public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        [StructLayout(LayoutKind.Sequential)] public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern int GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);

        [StructLayout(LayoutKind.Sequential)] public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateEvent(ref SECURITY_ATTRIBUTES sa, bool manual, bool initial, string name);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int rev, out IntPtr sd, out int size);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr h);

        public static void SetDwm(IntPtr h, int attr, int value) { DwmSetWindowAttribute(h, attr, ref value, 4); }

        /// <summary>Per-pixel alpha for a borderless window: DWM honors the alpha channel of the
        /// redirection surface, so a transparent WebView2 shows the desktop through.</summary>
        public static void EnableAlphaComposition(IntPtr h)
        {
            var rgn = CreateRectRgn(0, 0, -1, -1);
            var bb = new DWM_BLURBEHIND { dwFlags = 3, fEnable = true, hRgnBlur = rgn };
            DwmEnableBlurBehindWindow(h, ref bb);
            DeleteObject(rgn);
            SetDwm(h, 33, 1);                          // DWMWA_WINDOW_CORNER_PREFERENCE = DONOTROUND
            SetDwm(h, 34, unchecked((int)0xFFFFFFFE)); // DWMWA_BORDER_COLOR = NONE
            SetDwm(h, 3, 1);                           // DWMWA_TRANSITIONS_FORCEDISABLED
        }

        public static int ExStyle(IntPtr h) { return (int)(long)GetWindowLongPtr(h, GWL_EXSTYLE); }
        public static void SetExStyle(IntPtr h, int v) { SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(v)); }
    }
}
