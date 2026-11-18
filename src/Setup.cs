using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace BlitzAdblock {
    internal static class Setup {
        internal static string FindBlitz() {
            // Installed copies live in Blitz/BlitzAdblockPatch/versions/<digest>.
            var version = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            if (version.Parent != null && version.Parent.Name == "versions" &&
                version.Parent.Parent != null && version.Parent.Parent.Name == "BlitzAdblockPatch" &&
                version.Parent.Parent.Parent != null && Program.IsBlitz(version.Parent.Parent.Parent.FullName))
                return version.Parent.Parent.Parent.FullName;
            return Program.Detect();
        }

        static string Digest(string path) {
            using (var file = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        static void Store(string directory, string name, Stream content) {
            string path = Path.Combine(directory, name);
            string staged = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    content.CopyTo(output);
                if (!File.Exists(path)) File.Move(staged, path);
                else if (Digest(staged) != Digest(path)) throw new IOException("Installed file differs from this build: " + path);
            } finally { File.Delete(staged); }
        }

        internal static void Configure(bool restore, string root) {
            if (restore) {
                string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Uninstall Blitz Adblock Patch.lnk");
                if (!File.Exists(link)) throw new FileNotFoundException("The patch uninstall shortcut was not found.", link);
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
                return;
            }

            if (!Program.IsBlitz(root)) throw new IOException("Select the installation containing Blitz.exe and resources/app.asar.");
            string folder = Path.Combine(root, "BlitzAdblockPatch");
            string text = "Install Blitz Adblock Patch in " + folder + "?\n\nSetup updates your existing Blitz shortcuts and sign-in entry, and adds an uninstall shortcut to Start.";
            if (MessageBox.Show(text, "Blitz Adblock Patch", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string source = Assembly.GetExecutingAssembly().Location;
            string version = Path.Combine(folder, "versions", Digest(source));
            Directory.CreateDirectory(version);
            string helper = Path.Combine(version, "BlitzAdblock.exe");
            using (var input = File.OpenRead(source)) Store(version, "BlitzAdblock.exe", input);
            foreach (string name in new string[] { "integrate.ps1", "uninstall.ps1", "files.ps1" })
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) Store(version, name, input);
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var info = new ProcessStartInfo(powershell, "-NoProfile -File \"" + Path.Combine(version, "integrate.ps1") +
                "\" -Action Apply -LauncherPath \"" + helper + "\"") { UseShellExecute = false };
            using (var child = Process.Start(info)) {
                child.WaitForExit();
                if (child.ExitCode != 0) throw new IOException("Could not complete patch setup. Reopen the installer to retry.");
            }
            Console.WriteLine("Installed Blitz Adblock Patch in " + folder);
        }
    }
}
