using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Map = System.Collections.Generic.Dictionary<string, object>;

namespace BlitzAdblock {
    // Talks only to the copy of Blitz started by this launcher, over inherited
    // anonymous pipes. CSS is embedded at build time. No script evaluation.
    internal sealed class StyleSession {
        readonly Stream output;
        readonly Stream input;
        readonly string css;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        readonly Dictionary<int, Action<Map>> replies = new Dictionary<int, Action<Map>>();
        readonly Dictionary<string, string> targets = new Dictionary<string, string>();
        readonly Dictionary<string, string> documents = new Dictionary<string, string>();
        static readonly HashSet<string> hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "blitzapp.gg", "blitz.gg", "www.blitz.gg", "agentselect.net", "championselect.net",
            "lolstats.com", "probuilds.net", "tftcomps.gg"
        };
        int next;
        internal bool Applied { get; private set; }

        internal StyleSession(Stream send, Stream receive, string style) {
            output = send; input = receive; css = style;
        }

        internal static bool IsDesktop(string url) {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.IsDefaultPort && hosts.Contains(uri.Host) &&
                Regex.IsMatch(uri.AbsolutePath, @"^/v?\d+\.\d+\.\d+(?:/|$)");
        }

        internal void Run() {
            Send("Target.setDiscoverTargets", new { discover = true, filter = new[] { new { type = "page" } } }, null, null);
            using (var reader = new BufferedStream(input, 32768))
            using (var message = new MemoryStream()) {
                int ch;
                while ((ch = reader.ReadByte()) >= 0) {
                    if (ch != 0) {
                        if (message.Length == json.MaxJsonLength) throw new IOException("Oversized Chromium protocol message.");
                        message.WriteByte((byte)ch);
                        continue;
                    }
                    if (message.Length == 0) continue;
                    Dispatch((Map)json.DeserializeObject(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)));
                    message.SetLength(0);
                }
            }
        }

        void Send(string method, object args, string session, Action<Map> reply) {
            int id = ++next;
            var packet = new Map { { "id", id }, { "method", method }, { "params", args } };
            if (session != null) packet.Add("sessionId", session);
            if (reply != null) replies.Add(id, reply);
            byte[] data = Encoding.UTF8.GetBytes(json.Serialize(packet) + "\0");
            output.Write(data, 0, data.Length);
        }

        void Dispatch(Map packet) {
            if (packet.ContainsKey("id")) {
                int id = Convert.ToInt32(packet["id"]);
                Action<Map> callback;
                if (replies.TryGetValue(id, out callback)) {
                    replies.Remove(id);
                    callback(packet.ContainsKey("result") ? (Map)packet["result"] : null);
                }
                return;
            }
            var args = (Map)packet["params"];
            string session = packet.ContainsKey("sessionId") ? (string)packet["sessionId"] : null;
            switch ((string)packet["method"]) {
                case "Target.targetCreated":
                case "Target.targetInfoChanged":
                    Observe((Map)args["targetInfo"]); break;
                case "Target.targetDestroyed":
                    Forget((string)args["targetId"]); break;
                case "Target.detachedFromTarget":
                    if (args.ContainsKey("targetId")) Forget((string)args["targetId"]);
                    documents.Remove((string)args["sessionId"]); break;
                case "Page.domContentEventFired":
                    if (session != null && documents.ContainsKey(session)) InspectFrame(session);
                    break;
            }
        }

        void Forget(string target) {
            string session;
            if (targets.TryGetValue(target, out session) && session != null) documents.Remove(session);
            targets.Remove(target);
        }

        void Observe(Map info) {
            string target = (string)info["targetId"];
            string active;
            bool known = targets.TryGetValue(target, out active);
            if (!Equals(info["type"], "page") || !IsDesktop((string)info["url"])) {
                if (active != null) Send("Target.detachFromTarget", new { sessionId = active }, null, null);
                Forget(target);
                return;
            }
            if (known) return;
            targets.Add(target, null);
            Send("Target.attachToTarget", new { targetId = target, flatten = true }, null, result => {
                if (result == null) { Forget(target); return; }
                string session = (string)result["sessionId"];
                if (!targets.ContainsKey(target)) { Send("Target.detachFromTarget", new { sessionId = session }, null, null); return; }
                targets[target] = session;
                documents[session] = null;
                Send("Page.enable", new {}, session, null);
                Send("DOM.enable", new {}, session, null);
                Send("CSS.enable", new {}, session, ready => { if (ready != null) InspectFrame(session); });
            });
        }

        void InspectFrame(string session) {
            Send("Page.getFrameTree", new {}, session, result => {
                if (result == null || !documents.ContainsKey(session)) return;
                var frame = (Map)((Map)result["frameTree"])["frame"];
                string loader = (string)frame["loaderId"];
                if (!IsDesktop((string)frame["url"]) || documents[session] == loader) return;
                documents[session] = loader;
                Send("CSS.createStyleSheet", new { frameId = frame["id"] }, session, sheet => {
                    if (sheet == null) { RetryDocument(session, loader); return; }
                    Send("CSS.setStyleSheetText", new { styleSheetId = sheet["styleSheetId"], text = css }, session, done => {
                        if (done == null) { RetryDocument(session, loader); return; }
                        if (!Applied) Console.WriteLine("Desktop filter active.");
                        Applied = true;
                    });
                });
            });
        }

        void RetryDocument(string session, string loader) {
            string current;
            if (documents.TryGetValue(session, out current) && current == loader) documents[session] = null;
        }
    }
}
