using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace BlitzAdblock {
    internal static class Launcher {
        // Hosts on shared CDNs are listed individually. The observed CloudFront
        // distribution serves the Blitz Prebid bootstrap and wrapper.
        internal static readonly string[] AdHosts = {
            "dn0qt3r0xannq.cloudfront.net", "imasdk.googleapis.com",
            "aditude.io", "doubleclick.net", "googlesyndication.com",
            "smartadserver.com", "amazon-adsystem.com", "primis.tech",
            "monetize-static.viralize.tv", "prebid.cloud"
        };

        internal static string HostRules() {
            var maps = new List<string>();
            foreach (string host in AdHosts) {
                maps.Add("MAP " + host + " ~NOTFOUND");
                maps.Add("MAP *." + host + " ~NOTFOUND");
            }
            return string.Join(", ", maps);
        }

        internal static void Run(string exe, bool startup, bool shortcut) {
            foreach (var process in Process.GetProcessesByName("Blitz")) {
                using (process) {
                    try { if (process.HasExited) continue; }
                    catch (System.ComponentModel.Win32Exception) { }
                    if (shortcut) {
                        if (!startup) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                        return;
                    }
                    throw new IOException("Exit Blitz from its tray menu first, then start Blitz (Adblock).");
                }
            }
            using (var send = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable))
            using (var receive = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable)) {
                var info = new ProcessStartInfo(exe) {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    Arguments = (startup ? "--autostart " : "") + "--remote-debugging-pipe --remote-debugging-io-pipes=" +
                        send.GetClientHandleAsString() + "," + receive.GetClientHandleAsString() +
                        " --host-resolver-rules=\"" + HostRules() + "\""
                };
                using (var blitz = Process.Start(info)) {
                    send.DisposeLocalCopyOfClientHandle();
                    receive.DisposeLocalCopyOfClientHandle();
                    // Drain the child's ordinary output without storing account or game data.
                    blitz.BeginOutputReadLine();
                    blitz.BeginErrorReadLine();
                    string css;
                    using (var r = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("filter.css"), Encoding.UTF8))
                        css = r.ReadToEnd();
                    var filter = new StyleSession(send, receive, css);
                    filter.Run();
                    if (!filter.Applied) throw new IOException("Blitz closed before the desktop filter could attach.");
                }
            }
        }
    }
}
