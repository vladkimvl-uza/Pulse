using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Pulse")]
[assembly: AssemblyProduct("Pulse")]
[assembly: AssemblyDescription("CPU, GPU and memory monitor widget")]
[assembly: AssemblyCompany("Pulse")]
[assembly: AssemblyCopyright("© 2026")]

namespace Pulse
{
    static class Program
    {
        [STAThread]
        static int Main(string[] argv)
        {
            int pi = Array.FindIndex(argv, a => a.Equals("--profile", StringComparison.OrdinalIgnoreCase));
            bool sandbox = pi >= 0 && pi + 1 < argv.Length;
            if (sandbox) Paths.UseProfile(argv[pi + 1]);
            Paths.Ensure();
            var args = new HashSet<string>(argv, StringComparer.OrdinalIgnoreCase);
            bool elevated = Elevation.IsElevated();
            Log.Write("start " + string.Join(" ", argv) + (elevated ? " [elevated]" : ""));

            if (args.Contains("--setup")) return Elevation.RunSetup(!args.Contains("--autostart-off"));
            if (args.Contains("--uninstall")) return Uninstall();
            if (args.Contains("--quit")) return QuitRunning();
            if (args.Contains("--remove-task")) { if (elevated && Elevation.TaskExists()) Elevation.DeleteTask(); return 0; }

            bool replace = args.Contains("--replace");
            if (sandbox)
            {
                // Test instance: no single-instance lock, no task, no autostart, no global hotkeys.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new PulseApp(true, elevated, true));
                return 0;
            }
            // With sensors enabled, a normal launch is redirected to the elevated logon task (no UAC prompt).
            if (!elevated && !replace && !args.Contains("--task") && !args.Contains("--no-task") && Elevation.TaskExists())
            {
                if (Instance.SignalShow()) return 0;
                if (Elevation.RunTask()) return 0;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("FATAL " + e.ExceptionObject);
            Application.ThreadException += (s, e) => Log.Error("thread", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            using (var inst = Instance.Acquire(elevated, replace))
            {
                if (inst == null) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var app = new PulseApp(args.Contains("--dev"), elevated, false);
                inst.OnShow = () => app.Post(app.ShowFromOtherInstance);
                inst.OnQuit = () => app.Post(app.Quit);
                Application.Run(app);
            }
            Log.Write("exit");
            return 0;
        }

        /// <summary>For the installer: ask a running Pulse (any copy, any privilege level) to exit and wait for it.</summary>
        static int QuitRunning()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(@"Local\Pulse.Quit.v1", System.Security.AccessControl.EventWaitHandleRights.Modify | System.Security.AccessControl.EventWaitHandleRights.Synchronize))
                    ev.Set();
            }
            catch { return 0; } // nothing running
            for (int i = 0; i < 40; i++)
            {
                try { using (Mutex.OpenExisting(@"Local\Pulse.Instance.v1")) { } }
                catch (WaitHandleCannotBeOpenedException) { return 0; }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(250);
            }
            return 0;
        }

        static int Uninstall()
        {
            if (Elevation.TaskExists())
            {
                if (!Elevation.IsElevated())
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.Exe, "--uninstall") { UseShellExecute = true, Verb = "runas" }); }
                    catch { }
                    return 0;
                }
                Elevation.DeleteTask();
            }
            Elevation.SetRunKey(false);
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pulse.lnk")); } catch { }
            return 0;
        }
    }
}
