param([string]$Name = 'Pulse')
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class PulseWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  public static List<string> Find(uint pid) {
    var res = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); var sb = new StringBuilder(64); GetClassName(h, sb, 64);
        res.Add(string.Format("{0},{1},{2},{3} ex=0x{4:X} {5}", r.L, r.T, r.R - r.L, r.B - r.T, GetWindowLong(h, -20), sb)); }
      return true; }, IntPtr.Zero);
    return res;
  }
}
'@ -ErrorAction SilentlyContinue
[PulseWin]::SetProcessDPIAware() | Out-Null
foreach ($p in Get-Process $Name -ErrorAction SilentlyContinue) { [PulseWin]::Find([uint32]$p.Id) }
