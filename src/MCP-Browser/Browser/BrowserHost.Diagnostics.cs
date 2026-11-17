using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCP_Browser.Browser
{
    // Console / network capture through the Chrome DevTools Protocol.
    public partial class BrowserHost
    {
        private async Task EnableDiagnosticsAsync(BrowserTab tab)
        {
            try
            {
                await tab.Core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                await tab.Core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                await tab.Core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

                Subscribe(tab, "Runtime.consoleAPICalled", json => OnConsoleApi(tab, json));
                Subscribe(tab, "Runtime.exceptionThrown", json => OnExceptionThrown(tab, json));
                Subscribe(tab, "Log.entryAdded", json => OnLogEntryAdded(tab, json));
                Subscribe(tab, "Network.requestWillBeSent", json => OnRequestWillBeSent(tab, json));
                Subscribe(tab, "Network.loadingFinished", json => OnLoadingFinished(tab, json));
                Subscribe(tab, "Network.responseReceived", json => OnResponseReceived(tab, json));
                Subscribe(tab, "Network.loadingFailed", json => OnLoadingFailed(tab, json));
            }
            catch (Exception ex)
            {
                Diag.Log("EnableDiagnosticsAsync failed: " + ex.Message);
            }
        }

        private static void Subscribe(BrowserTab tab, string eventName, Action<string> handler)
        {
            var rx = tab.Core.GetDevToolsProtocolEventReceiver(eventName);
            rx.DevToolsProtocolEventReceived += (s, e) =>
            {
                try { handler(e.ParameterObjectAsJson); }
                catch (Exception ex) { Diag.Log($"CDP handler {eventName} failed: {ex.Message}"); }
            };
            tab.Receivers.Add(rx);
        }

        private static string DescribeArg(JToken arg)
        {
            if (arg == null) return "";
            var val = arg["value"];
            if (val != null)
                return val.Type == JTokenType.String ? val.Value<string>() : val.ToString(Formatting.None);
            var desc = arg["description"];
            if (desc != null) return desc.Value<string>();
            var preview = arg["preview"]?["description"];
            if (preview != null) return preview.Value<string>();
            return arg["type"]?.Value<string>() ?? "";
        }

        private static void OnConsoleApi(BrowserTab tab, string json)
        {
            var o = JObject.Parse(json);
            string type = o["type"]?.Value<string>() ?? "log";
            var args = o["args"] as JArray;
            string text = args == null
                ? ""
                : string.Join(" ", args.Select(DescribeArg).Where(s => !string.IsNullOrEmpty(s)));

            tab.Add(new LogEntry
            {
                Kind = "console",
                Level = type,
                Text = text,
                TimeUtc = DateTime.UtcNow
            });
        }

        private static void OnExceptionThrown(BrowserTab tab, string json)
        {
            var o = JObject.Parse(json);
            var ex = o["exceptionDetails"];
            string text = ex?["exception"]?["description"]?.Value<string>()
                          ?? ex?["text"]?.Value<string>()
                          ?? "Uncaught exception";
            tab.Add(new LogEntry
            {
                Kind = "console",
                Level = "error",
                Text = text,
                Url = ex?["url"]?.Value<string>(),
                TimeUtc = DateTime.UtcNow
            });
        }

        private static void OnLogEntryAdded(BrowserTab tab, string json)
        {
            var entry = JObject.Parse(json)["entry"];
            if (entry == null) return;
            string level = entry["level"]?.Value<string>() ?? "info";
            // Network failures also surface here; Network.* handlers cover those
            // with better detail, so only keep genuine browser log messages.
            string source = entry["source"]?.Value<string>() ?? "";
            if (source == "network") return;

            tab.Add(new LogEntry
            {
                Kind = "console",
                Level = level,
                Text = entry["text"]?.Value<string>() ?? "",
                Url = entry["url"]?.Value<string>(),
                TimeUtc = DateTime.UtcNow
            });
        }

        private static void OnRequestWillBeSent(BrowserTab tab, string json)
        {
            var o = JObject.Parse(json);
            tab.TrackRequest(o["requestId"]?.Value<string>(), o["request"]?["url"]?.Value<string>());
        }

        private static void OnLoadingFinished(BrowserTab tab, string json)
        {
            tab.TakeRequestUrl(JObject.Parse(json)["requestId"]?.Value<string>());
        }

        private static void OnResponseReceived(BrowserTab tab, string json)
        {
            var resp = JObject.Parse(json)["response"];
            if (resp == null) return;
            int status = resp["status"]?.Value<int>() ?? 0;
            if (status < 400) return;   // only failures are worth the context

            tab.Add(new LogEntry
            {
                Kind = "network",
                Level = status >= 500 ? "error" : "warning",
                Text = resp["statusText"]?.Value<string>() ?? "HTTP " + status,
                Url = resp["url"]?.Value<string>(),
                Status = status,
                TimeUtc = DateTime.UtcNow
            });
        }

        private static void OnLoadingFailed(BrowserTab tab, string json)
        {
            var o = JObject.Parse(json);
            string url = tab.TakeRequestUrl(o["requestId"]?.Value<string>());

            bool canceled = o["canceled"]?.Value<bool>() ?? false;
            if (canceled) return;

            string type = o["type"]?.Value<string>();
            string error = o["errorText"]?.Value<string>() ?? "request failed";

            tab.Add(new LogEntry
            {
                Kind = "network",
                Level = "error",
                Text = string.IsNullOrEmpty(type) ? error : $"{error} ({type})",
                Url = url,
                TimeUtc = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Captured console + network entries for the active tab, newest last.
        /// Safe from any thread; returns an empty list when no window is open.
        /// </summary>
        public List<LogEntry> GetLogs(string kind = null, string minLevel = null)
        {
            List<LogEntry> all = InvokeUi(() => HasWindow ? ActiveTab.Snapshot() : new List<LogEntry>());
            IEnumerable<LogEntry> q = all;
            if (!string.IsNullOrEmpty(kind) && kind != "all")
                q = q.Where(e => e.Kind == kind);
            if (minLevel == "error")
                q = q.Where(e => e.Level == "error");
            else if (minLevel == "warning")
                q = q.Where(e => e.Level == "error" || e.Level == "warning");
            return q.ToList();
        }

        public void ClearLogs()
        {
            InvokeUi(() =>
            {
                if (HasWindow) ActiveTab.ClearLogs();
                return true;
            });
        }

        /// <summary>URL of the active tab, or null when no window is open. Safe from any thread.</summary>
        public string CurrentUrl
        {
            get
            {
                try { return InvokeUi(() => HasWindow ? ActiveTab.Core?.Source : null); }
                catch { return null; }
            }
        }
    }
}
