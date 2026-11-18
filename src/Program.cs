using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BlitzAdblock {
    internal static class Program {
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [STAThread]
        static int Main(string[] args) {
            bool menu = args.Length == 0 && !Console.IsInputRedirected;
            bool shortcut = args.Length > 0 && (args[0] == "launch" || args[0] == "startup");
            if (shortcut) FreeConsole();
            try {
                if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h")) {
                    Help(); return 0;
                }
                string action = args.Length == 0 ? "check" : args[0].ToLowerInvariant();
                if (menu) action = "install";
                if ((action == "install" || action == "uninstall") &&
                    (args.Length <= 1 || (args.Length == 3 && args[1] == "--dir"))) {
                    string installRoot = args.Length == 3 ? Path.GetFullPath(args[2]) : Setup.FindBlitz();
                    if (!IsBlitz(installRoot) && action == "install") {
                        using (var pick = new OpenFileDialog { Title = "Locate Blitz.exe", Filter = "Blitz desktop app|Blitz.exe" }) {
                            if (pick.ShowDialog() != DialogResult.OK) return 0;
                            installRoot = Path.GetDirectoryName(pick.FileName);
                        }
                    }
                    Setup.Configure(action == "uninstall", installRoot);
                    return 0;
                }
                if ((action != "check" && action != "apply" && action != "restore" && action != "run" && !shortcut) ||
                    !(args.Length <= 1 || (args.Length == 3 && args[1] == "--dir"))) {
                    Help(); return 2;
                }
                if (action == "restore") { Shortcut.Manage(action, null); return 0; }
                string root = args.Length == 3 ? Path.GetFullPath(args[2]) : Detect();
                if (menu && !IsBlitz(root)) {
                    using (var pick = new OpenFileDialog { Title = "Locate Blitz", Filter = "Blitz desktop app|Blitz.exe" }) {
                        if (pick.ShowDialog() != DialogResult.OK) return 0;
                        root = Path.GetDirectoryName(pick.FileName);
                    }
                }
                if (!IsBlitz(root)) throw new DirectoryNotFoundException("Blitz installation not found. Use --dir with the folder containing Blitz.exe.");
                string exe = Path.Combine(root, "Blitz.exe");
                Console.WriteLine("Blitz " + FileVersionInfo.GetVersionInfo(exe).ProductVersion + " at " + root);
                switch (action) {
                    case "check":
                        Console.WriteLine("Desktop shortcut: " + (File.Exists(Shortcut.Location) ? "present" : "absent"));
                        break;
                    case "apply": Shortcut.Manage(action, root); break;
                    default: Launcher.Run(exe, action == "startup", shortcut); break;
                }
                return 0;
            } catch (Exception e) {
                Console.Error.WriteLine("Blitz Adblock: " + e.Message);
                if (shortcut || menu) MessageBox.Show(e.Message, "Blitz Adblock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;

            }
        }

        static void Help() {
            Console.WriteLine("BlitzAdblock.exe check|apply|run|restore [--dir \"C:\\Path\\To\\Blitz\"]");
            Console.WriteLine("BlitzAdblock.exe install|uninstall (with confirmation)");
        }

        internal static bool IsBlitz(string folder) {
            return folder != null && File.Exists(Path.Combine(folder, "Blitz.exe")) &&
                File.Exists(Path.Combine(folder, "resources", "app.asar"));
        }

        internal static string Detect() {
            var paths = new List<string>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            paths.Add(Path.Combine(local, "Programs", "Blitz"));
            paths.Add(Path.Combine(local, "Blitz"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blitz"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Blitz"));
            string standard = paths.Find(IsBlitz);
            if (standard != null) return standard;
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                foreach (string key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }) {
                    using (var uninstall = hive.OpenSubKey(key)) {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames()) {
                            using (var item = uninstall.OpenSubKey(name)) {
                                if (item != null && string.Equals(item.GetValue("DisplayName") as string, "Blitz", StringComparison.OrdinalIgnoreCase)) {
                                    string value = item.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrWhiteSpace(value)) paths.Add(value.Trim('"'));
                                }
                            }
                        }
                    }
                }
            }
            return paths.Find(IsBlitz);
        }
    }
}
