using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Pulse
{
    /// <summary>
    /// CPU temperature, clocks and power need a kernel driver (PawnIO) and an elevated process.
    /// One UAC prompt installs PawnIO and registers a "highest privileges" logon task; after that
    /// Pulse always starts elevated through the task without asking again.
    /// </summary>
    static class Elevation
    {
        public const string TaskName = "Pulse System Monitor";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "Pulse";
        const string PawnUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        const string PawnSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

        public static bool IsElevated()
        {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool PawnIOInstalled()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) if (k != null) return true;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO")) if (k != null) return true;
            }
            catch { }
            return false;
        }

        static int Run(string exe, string args, out string output, int timeoutMs = 15000)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                var sb = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } output = "timeout"; return -1; }
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        static string Schtasks { get { return Path.Combine(Environment.SystemDirectory, "schtasks.exe"); } }

        public static bool TaskExists()
        {
            try { string o; return Run(Schtasks, "/query /tn \"" + TaskName + "\"", out o, 8000) == 0; } catch { return false; }
        }

        /// <summary>Whether the task's logon trigger is enabled (= autostart while the task mode is in use).</summary>
        public static bool TaskAutostart()
        {
            try
            {
                string xml;
                if (Run(Schtasks, "/query /tn \"" + TaskName + "\" /xml", out xml, 8000) != 0) return false;
                int i = xml.IndexOf("<LogonTrigger>", StringComparison.Ordinal);
                if (i < 0) return false;
                int j = xml.IndexOf("</LogonTrigger>", i, StringComparison.Ordinal);
                return !xml.Substring(i, j - i).Contains("<Enabled>false</Enabled>");
            }
            catch { return false; }
        }

        public static bool RunTask()
        {
            try { string o; return Run(Schtasks, "/run /tn \"" + TaskName + "\"", out o, 8000) == 0; } catch { return false; }
        }

        /// <summary>Creates or replaces the logon task. Requires elevation.</summary>
        public static void RegisterTask(bool autostart)
        {
            var user = WindowsIdentity.GetCurrent().Name;
            string esc(string s) { return System.Security.SecurityElement.Escape(s); }
            var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                " <RegistrationInfo><Description>Pulse — CPU, GPU and memory monitor widget</Description></RegistrationInfo>\n" +
                " <Triggers><LogonTrigger><Enabled>" + (autostart ? "true" : "false") + "</Enabled><UserId>" + esc(user) + "</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>\n" +
                " <Principals><Principal id=\"Author\"><UserId>" + esc(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                " <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable>" +
                "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority></Settings>\n" +
                " <Actions Context=\"Author\"><Exec><Command>" + esc(Paths.Exe) + "</Command><Arguments>--task</Arguments><WorkingDirectory>" + esc(Paths.AppDir.TrimEnd('\\')) + "</WorkingDirectory></Exec></Actions>\n" +
                "</Task>\n";
            var tmp = Path.Combine(Path.GetTempPath(), "pulse-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                string o;
                int rc = Run(Schtasks, "/create /tn \"" + TaskName + "\" /xml \"" + tmp + "\" /f", out o);
                if (rc != 0) throw new InvalidOperationException("schtasks /create: " + o.Trim());
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void DeleteTask()
        {
            string o; Run(Schtasks, "/delete /tn \"" + TaskName + "\" /f", out o);
        }

        public static bool RunKeyEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunValue) != null; } catch { return false; }
        }

        public static void SetRunKey(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunValue, "\"" + Paths.Exe + "\" --autostart");
                    else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
                }
            }
            catch (Exception ex) { Log.Error("run key", ex); }
        }

        /// <summary>Starts "Pulse.exe --setup" through UAC. Returns the process, or null if the user declined.</summary>
        public static Process LaunchSetup(bool autostart)
        {
            try
            {
                return Process.Start(new ProcessStartInfo(Paths.Exe, "--setup " + (autostart ? "--autostart-on" : "--autostart-off"))
                    { UseShellExecute = true, Verb = "runas", WorkingDirectory = Paths.AppDir });
            }
            catch (Win32Exception) { return null; } // 1223: cancelled at the UAC prompt
        }

        /// <summary>Runs in the elevated "--setup" process.</summary>
        public static int RunSetup(bool autostart)
        {
            try
            {
                if (!IsElevated()) { Log.Write("setup: not elevated"); return 5; }
                if (!PawnIOInstalled()) InstallPawnIO();
                if (!PawnIOInstalled()) { Log.Write("setup: PawnIO still missing"); return 2; }
                RegisterTask(autostart);
                SetRunKey(false); // the task replaces the plain Run entry
                // Start an elevated instance that takes over from the running one.
                Process.Start(new ProcessStartInfo(Paths.Exe, "--replace") { UseShellExecute = false, WorkingDirectory = Paths.AppDir });
                return 0;
            }
            catch (Exception ex) { Log.Error("setup", ex); return 1; }
        }

        static void InstallPawnIO()
        {
            // Preferred: winget (verifies the package hash from its manifest).
            try
            {
                string o;
                int rc = Run("winget", "install --id namazso.PawnIO -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", out o, 240000);
                Log.Write("winget PawnIO rc=" + rc + " " + o.Trim().Replace("\r", "").Replace("\n", " | "));
                if (PawnIOInstalled()) return;
            }
            catch (Exception ex) { Log.Write("winget unavailable: " + ex.Message); }

            // Fallback: the pinned official installer, verified by SHA-256 before it runs.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var dir = Path.Combine(Path.GetTempPath(), "pulse-pawnio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "PawnIO_setup.exe");
            using (var wc = new WebClient()) wc.DownloadFile(PawnUrl, file);
            string hash;
            using (var sha = SHA256.Create()) using (var fs = File.OpenRead(file)) hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            if (!hash.Equals(PawnSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("PawnIO installer hash mismatch: " + hash);
            string outp;
            int code = Run(file, "-install -silent", out outp, 240000);
            Log.Write("PawnIO setup rc=" + code);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// Single instance with hand-over: a second launch asks the running one to show itself; an
    /// elevated launch replaces a non-elevated one (that is how "enable sensors" restarts Pulse).
    /// </summary>
    sealed class Instance : IDisposable
    {
        const string MutexName = @"Local\Pulse.Instance.v1", ShowName = @"Local\Pulse.Show.v1", QuitName = @"Local\Pulse.Quit.v1", ElevName = @"Local\Pulse.Elevated.v1";

        Mutex mutex;
        EventWaitHandle showEv, quitEv, elevEv;
        Thread waiter;
        volatile bool disposed;
        public Action OnShow, OnQuit;

        public static bool SignalShow() { return Signal(ShowName); }

        static bool Signal(string name)
        {
            try
            {
                using (var e = EventWaitHandle.OpenExisting(name, System.Security.AccessControl.EventWaitHandleRights.Modify | System.Security.AccessControl.EventWaitHandleRights.Synchronize))
                { e.Set(); return true; }
            }
            catch { return false; }
        }

        static bool Exists(string name)
        {
            try { using (EventWaitHandle.OpenExisting(name, System.Security.AccessControl.EventWaitHandleRights.Synchronize)) return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        /// <summary>Events at medium integrity so a non-elevated launch can signal an elevated instance.</summary>
        static EventWaitHandle CreateShared(string name)
        {
            string sid;
            using (var id = WindowsIdentity.GetCurrent()) sid = id.User.Value;
            IntPtr sd; int size;
            if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;" + sid + ")(A;;GA;;;SY)(A;;GA;;;BA)S:(ML;;NW;;;ME)", 1, out sd, out size))
                return new EventWaitHandle(false, EventResetMode.AutoReset, name);
            try
            {
                var sa = new Native.SECURITY_ATTRIBUTES { nLength = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES)), lpSecurityDescriptor = sd };
                var h = Native.CreateEvent(ref sa, false, false, name);
                if (h == IntPtr.Zero) return new EventWaitHandle(false, EventResetMode.AutoReset, name);
                return new EventWaitHandle(false, EventResetMode.AutoReset) { SafeWaitHandle = new SafeWaitHandle(h, true) };
            }
            finally { Native.LocalFree(sd); }
        }

        public static Instance Acquire(bool elevated, bool replace)
        {
            bool created = false;
            Mutex m = null;
            try { m = new Mutex(true, MutexName, out created); }
            catch (UnauthorizedAccessException) { }
            if (!created)
            {
                bool otherElevated = Exists(ElevName);
                if ((replace || (elevated && !otherElevated)) && m != null)
                {
                    Signal(QuitName);
                    try { if (!m.WaitOne(10000)) { m.Dispose(); return null; } }
                    catch (AbandonedMutexException) { }
                }
                else
                {
                    Signal(ShowName);
                    if (m != null) m.Dispose();
                    return null;
                }
            }
            var inst = new Instance { mutex = m, showEv = CreateShared(ShowName), quitEv = CreateShared(QuitName) };
            if (elevated) inst.elevEv = CreateShared(ElevName);
            inst.waiter = new Thread(inst.Wait) { IsBackground = true, Name = "Pulse instance" };
            inst.waiter.Start();
            return inst;
        }

        void Wait()
        {
            var handles = new WaitHandle[] { showEv, quitEv };
            while (!disposed)
            {
                int i;
                try { i = WaitHandle.WaitAny(handles); } catch { return; }
                if (disposed) return;
                var a = i == 0 ? OnShow : OnQuit;
                if (a != null) a();
            }
        }

        public void Dispose()
        {
            disposed = true;
            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Dispose(); } catch { }
            foreach (var e in new[] { showEv, quitEv, elevEv }) { try { if (e != null) e.Dispose(); } catch { } }
        }
    }
}
