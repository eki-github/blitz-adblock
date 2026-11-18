using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace BlitzAdblock {
    internal static class Shortcut {
        const string Description = "Blitz Adblock desktop launcher";
        internal static string Location {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Blitz (Adblock).lnk"); }
        }

        internal static void Manage(string action, string root) {
            using (var mutex = new Mutex(false, @"Local\BlitzAdblock-Shortcut")) {
                try { if (!mutex.WaitOne(0)) throw new IOException("Another shortcut operation is running."); }
                catch (AbandonedMutexException) { }
                try { ManageLocked(action, root); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        static void ManageLocked(string action, string root) {
            string path = Location;
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            try {
                bool exists = File.Exists(path);
                if (exists) {
                    dynamic previous = shell.CreateShortcut(path);
                    try {
                        if (previous.Description != Description) throw new IOException("A different shortcut already uses " + path);
                    } finally { Marshal.FinalReleaseComObject(previous); }
                }
                if (action == "restore") {
                    if (exists) File.Delete(path);
                    Console.WriteLine("Use the original Blitz shortcut to start normally.");
                    return;
                }
                string temp = Path.Combine(Path.GetDirectoryName(path), ".blitz-" + Guid.NewGuid().ToString("N") + ".lnk");
                try {
                    dynamic link = shell.CreateShortcut(temp);
                    try {
                        link.TargetPath = Assembly.GetExecutingAssembly().Location;
                        link.Arguments = "launch --dir \"" + root.TrimEnd('\\') + "\"";
                        link.Description = Description;
                        link.IconLocation = Path.Combine(root, "Blitz.exe") + ",0";
                        link.WorkingDirectory = root;
                        link.WindowStyle = 7;
                        link.Save();
                    } finally { Marshal.FinalReleaseComObject(link); }
                    if (exists) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                } finally { File.Delete(temp); }
                Console.WriteLine("Desktop shortcut ready: " + path);
            } finally { Marshal.FinalReleaseComObject(shell); }
        }
    }
}
