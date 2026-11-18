using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Map = System.Collections.Generic.Dictionary<string, object>;

namespace BlitzAdblock {
    internal static class FilterTests {
        static void Expect(bool condition, string message) {
            if (!condition) throw new Exception(message);
        }

        static int Main() {
            try {
                const string view = "https://blitzapp.gg/v3.0.25/lol/profile";
                Expect(StyleSession.IsDesktop(view), "Main view missing");
                foreach (string other in new[] { "https://blitzapp.gg.example/v3.0.25/lol", "file:///v3.0.25/", "https://blitz.gg/login", "https://127.0.0.1/v3.0.25/lol" })
                    Expect(!StyleSession.IsDesktop(other), "Unexpected view: " + other);
                var rules = Launcher.HostRules().Split(new[] { ", " }, StringSplitOptions.None);
                Expect(rules.Contains("MAP imasdk.googleapis.com ~NOTFOUND"), "IMA rule missing");
                Expect(rules.Contains("MAP *.doubleclick.net ~NOTFOUND"), "Ad subdomain rule missing");
                foreach (string rule in rules) {
                    Expect(!rule.Contains("*.cloudfront.net") && !rule.Contains("blitz.gg") &&
                        !rule.Contains("127.0.0.1") && !rule.Contains("*.googleapis.com"), "Overbroad rule");
                }
                var json = new JavaScriptSerializer();
                var events = new List<object> {
                    new { method = "Target.targetCreated", @params = new { targetInfo = new { targetId = "overlay", type = "page", url = "file:///overlay.html" } } },
                    new { method = "Target.targetInfoChanged", @params = new { targetInfo = new { targetId = "main", type = "page", url = view } } },
                    new { id = 2, result = new { sessionId = "s" } },
                    new { id = 5, result = new {} },
                    Frame(6, view, "first"), new { id = 7, result = new { styleSheetId = "sheet-a" } },
                    new { id = 8, result = new {} },
                    DomReady(), Frame(9, view, "first"), DomReady(), Frame(10, view, "second"),
                    new { id = 11, result = new { styleSheetId = "sheet-b" } }, new { id = 12, result = new {} },
                    new { method = "Target.targetInfoChanged", @params = new { targetInfo = new { targetId = "main", type = "page", url = "https://example.com/" } } }
                };
                byte[] input = Encoding.UTF8.GetBytes(string.Join("\0", events.Select(json.Serialize)) + "\0");
                using (var output = new MemoryStream()) {
                    var session = new StyleSession(output, new MemoryStream(input), ".ad { display:none }");
                    session.Run();
                    var commands = Encoding.UTF8.GetString(output.ToArray()).Split(new[] {'\0'}, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => (Map)json.DeserializeObject(s)).ToArray();
                    Expect(session.Applied, "No successful application");
                    Expect(commands.Count(c => Equals(c["method"], "Target.attachToTarget")) == 1, "Attached to unrelated view");
                    Expect(commands.Count(c => Equals(c["method"], "CSS.setStyleSheetText")) == 2, "Reload handling failed");
                    Expect(Equals(commands.Last()["method"], "Target.detachFromTarget"), "External navigation stayed attached");
                }
                Console.WriteLine("Filter tests passed.");
                return 0;
            } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        static object DomReady() {
            return new { method = "Page.domContentEventFired", sessionId = "s", @params = new {} };
        }

        static object Frame(int id, string url, string loader) {
            return new { id = id, result = new { frameTree = new { frame = new { id = "frame", url = url, loaderId = loader } } } };
        }
    }
}
