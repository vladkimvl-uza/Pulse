using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pulse Setup")]
[assembly: AssemblyProduct("Pulse")]
[assembly: AssemblyDescription("Pulse Setup")]
[assembly: AssemblyCompany("Pulse")]
[assembly: AssemblyCopyright("© 2026")]

namespace PulseSetup
{
    /// <summary>
    /// Entry point kept free of WebView2 types: the WebView2 assemblies live inside this exe as
    /// resources and are resolved on demand, so the installer is a single file.
    /// </summary>
    static class Program
    {
        public const string Version = BuildInfo.Version;

        [STAThread]
        static int Main(string[] argv)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name + ".dll";
                var bytes = Res.Bytes(name);
                return bytes != null ? Assembly.Load(bytes) : null;
            };
            return Runner.Run(argv);
        }
    }

    static class Res
    {
        public static byte[] Bytes(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }
        public static Stream Open(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
    }

    static class Runner
    {
        public static int Run(string[] argv)
        {
            var args = new HashSet<string>(argv, StringComparer.OrdinalIgnoreCase);
            bool uninstall = args.Contains("--uninstall");
            // An uninstaller cannot delete the folder it runs from: continue from a temp copy.
            if (uninstall && !args.Contains("--from-temp"))
            {
                var self = Assembly.GetExecutingAssembly().Location;
                var tmp = Path.Combine(Path.GetTempPath(), "PulseUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(self, tmp, true);
                Process.Start(new ProcessStartInfo(tmp, "--uninstall --from-temp --dir \"" + Path.GetDirectoryName(self) + "\"" + (args.Contains("--quiet") ? " --quiet" : "")) { UseShellExecute = false });
                return 0;
            }
            int di = Array.FindIndex(argv, a => a.Equals("--dir", StringComparison.OrdinalIgnoreCase));
            string dir = di >= 0 && di + 1 < argv.Length ? argv[di + 1] : null;

            SetupWindow.Demo = args.Contains("--demo"); SetupWindow.AutoRun = SetupWindow.Demo && args.Contains("--auto");
            if (args.Contains("--quiet"))
            {
                var eng = new Engine(dir ?? Engine.DefaultDir, null);
                if (uninstall) eng.Uninstall(false); else eng.Install(new Options { Autostart = true, Sensors = true, Desktop = false, Launch = !args.Contains("--no-launch") });
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // The pages need WebView2 (Windows 11 ships it); older systems get it first, behind a small native window.
            if (!Engine.WebView2Installed()) Prep.Run(Engine.InstallWebView2);
            var ui = UiFiles.Extract();
            Application.Run(new SetupWindow(ui, uninstall, dir ?? Engine.InstalledDir() ?? Engine.DefaultDir));
            return 0;
        }
    }

    /// <summary>The setup pages ship inside the exe as a zip and are unpacked to a temp folder.</summary>
    static class UiFiles
    {
        public static string Extract()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PulseSetup-" + Program.Version);
            if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            using (var z = new ZipArchive(Res.Open("setup-ui.zip"))) z.ExtractToDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "WebView2Loader.dll"), Res.Bytes("WebView2Loader.dll"));
            return dir;
        }
    }

    /// <summary>Plain dark window shown while WebView2 is being installed on systems without it.</summary>
    sealed class Prep : Form
    {
        public static void Run(Action work)
        {
            using (var f = new Prep())
            {
                f.Shown += (s, e) => Task.Run(work).ContinueWith(_ => f.BeginInvoke(new Action(f.Close)));
                Application.Run(f);
            }
        }

        Prep()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 17, 20);
            ForeColor = Color.FromArgb(245, 245, 247);
            Size = new Size(420, 120);
            Text = "Pulse Setup";
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f),
                Text = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru"
                    ? "Подготовка установки…\nУстанавливается Microsoft Edge WebView2"
                    : "Preparing setup…\nInstalling Microsoft Edge WebView2",
            });
        }
    }

    sealed class Options
    {
        public bool Autostart = true, Sensors = true, Desktop, Launch = true;
        /// <summary>"laptop" or "desktop": what the app sets itself up for (battery widget, eco on battery).</summary>
        public string Device = Pulse.Device.Kind();
        public string Lang = "ru";
    }

    /// <summary>Install / uninstall steps. Progress goes to the window as (fraction, status key).</summary>
    sealed class Engine
    {
        public const string Exe = "Pulse.exe";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Pulse";
        public static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pulse"); } }

        readonly string dir;
        readonly Action<double, string> progress;

        public Engine(string dir, Action<double, string> progress) { this.dir = dir; this.progress = progress ?? ((a, b) => { }); }

        public static string InstalledDir()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(UninstallKey)) return k != null ? k.GetValue("InstallLocation") as string : null; }
            catch { return null; }
        }

        public static bool IsInstalled() { return InstalledDir() != null; }

        public static string InstalledVersion()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(UninstallKey)) return k != null ? k.GetValue("DisplayVersion") as string : null; }
            catch { return null; }
        }

        void Report(double f, string key) { progress(Math.Max(0, Math.Min(1, f)), key); }

        // ---------------------------------------------------------------- install

        public void Install(Options o)
        {
            Report(0.02, "closing");
            QuitRunning();

            Report(0.05, "copying");
            Directory.CreateDirectory(dir);
            using (var z = new ZipArchive(Res.Open("payload.zip")))
            {
                long total = Math.Max(1, z.Entries.Sum(e => e.Length)), done = 0;
                foreach (var e in z.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(dir, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue; // no path tricks
                    if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    for (int attempt = 0; ; attempt++)
                    {
                        try { e.ExtractToFile(target, true); break; }
                        catch (IOException) when (attempt < 20) { Thread.Sleep(250); } // a closing Pulse may still hold a file
                    }
                    done += e.Length;
                    Report(0.05 + 0.55 * done / total, "copying");
                }
            }
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(dir, "Uninstall.exe"), true);
            File.WriteAllText(Path.Combine(dir, "install.json"), "{\"device\":\"" + o.Device + "\",\"version\":\"" + Program.Version + "\"}");

            Report(0.62, "shortcuts");
            var exe = Path.Combine(dir, Exe);
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Pulse.lnk"), exe);
            var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Pulse.lnk");
            if (o.Desktop) Shortcut(desk, exe); else TryDelete(desk);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pulse.lnk")); // older portable copy

            Report(0.68, "registering");
            RegisterUninstall(exe);

            if (!WebView2Installed())
            {
                Report(0.72, "webview2");
                InstallWebView2();
            }

            if (o.Sensors)
            {
                Report(0.8, "sensors");
                // Installs PawnIO if needed, registers the elevated sign-in task for this folder and starts Pulse.
                RunWait(exe, "--setup " + (o.Autostart ? "--autostart-on" : "--autostart-off"), 300000);
                if (!o.Launch) QuitRunning();
            }
            else
            {
                Report(0.8, "autostart");
                RunWait(exe, "--remove-task", 30000);
                SetRunKey(o.Autostart ? "\"" + exe + "\" --autostart" : null);
                // Started from the shell so it runs as the signed-in user, not as this elevated setup.
                if (o.Launch) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true });
            }
            Report(1, "done");
        }

        // ---------------------------------------------------------------- uninstall

        public void Uninstall(bool keepData)
        {
            Report(0.05, "closing");
            QuitRunning();
            var exe = Path.Combine(dir, Exe);
            Report(0.25, "unregistering");
            if (File.Exists(exe)) RunWait(exe, "--uninstall", 30000);
            SetRunKey(null);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Pulse.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Pulse.lnk"));
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }

            Report(0.5, "removing");
            for (int attempt = 0; attempt < 20 && Directory.Exists(dir); attempt++)
            {
                try { Directory.Delete(dir, true); } catch { Thread.Sleep(300); }
            }
            if (!keepData)
            {
                Report(0.85, "removingData");
                foreach (var d in new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse") })
                    try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
            Report(1, "removed");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Asks a running Pulse to quit through its own event and waits for its instance lock to vanish.</summary>
        public static void QuitRunning()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(@"Local\Pulse.Quit.v1", System.Security.AccessControl.EventWaitHandleRights.Modify | System.Security.AccessControl.EventWaitHandleRights.Synchronize))
                    ev.Set();
            }
            catch { return; }
            for (int i = 0; i < 40; i++)
            {
                try { using (Mutex.OpenExisting(@"Local\Pulse.Instance.v1")) { } }
                catch (WaitHandleCannotBeOpenedException) { return; }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(250);
            }
        }

        void RegisterUninstall(string exe)
        {
            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                long kb = 0;
                try { kb = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024; } catch { }
                var un = Path.Combine(dir, "Uninstall.exe");
                k.SetValue("DisplayName", "Pulse");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "Pulse");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + un + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + un + "\" --uninstall --quiet");
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        static void SetRunKey(string value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (value != null) k.SetValue("Pulse", value);
                    else if (k.GetValue("Pulse") != null) k.DeleteValue("Pulse");
                }
            }
            catch { }
        }

        static void Shortcut(string lnk, string target)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic s = sh.CreateShortcut(lnk);
                s.TargetPath = target;
                s.WorkingDirectory = Path.GetDirectoryName(target);
                s.Description = "Pulse — CPU, GPU, RAM monitor";
                s.IconLocation = target + ",0";
                s.Save();
            }
            catch { }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        static void RunWait(string exe, string args, int timeout)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) }))
                    p.WaitForExit(timeout);
            }
            catch { }
        }

        public static bool WebView2Installed()
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" })
                    try
                    {
                        using (var k = root.OpenSubKey(path))
                        {
                            var pv = k != null ? k.GetValue("pv") as string : null;
                            if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return true;
                        }
                    }
                    catch { }
            return false;
        }

        public static void InstallWebView2()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288;
                var f = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
                using (var wc = new System.Net.WebClient()) wc.DownloadFile("https://go.microsoft.com/fwlink/p/?LinkId=2124703", f);
                RunWait(f, "/silent /install", 600000);
            }
            catch { }
        }
    }
}
