using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MCP_Browser.Browser
{
    /// <summary>A single captured console message, dialog or network error.</summary>
    public class LogEntry
    {
        public string Kind { get; set; }      // "console" | "network"
        public string Level { get; set; }     // error | warning | info | log
        public string Text { get; set; }
        public string Url { get; set; }
        public int Status { get; set; }
        public DateTime TimeUtc { get; set; }

        public JObject ToJson()
        {
            var o = new JObject
            {
                ["kind"] = Kind,
                ["level"] = Level,
                ["text"] = Text,
                ["time"] = TimeUtc.ToString("HH:mm:ss.fff")
            };
            if (!string.IsNullOrEmpty(Url)) o["url"] = Url;
            if (Status > 0) o["status"] = Status;
            return o;
        }

        public override string ToString()
        {
            if (Kind == "network")
                return $"[network {(Status > 0 ? Status.ToString() : "failed")}] {Url} — {Text}";
            return $"[console {Level}] {Text}";
        }
    }

    /// <summary>One tab: a WebView2 control plus its diagnostics buffer.</summary>
    internal class BrowserTab
    {
        private const int MaxLogEntries = 300;
        private const int MaxTrackedRequests = 2000;

        public int Id;
        public WebView2 View;
        public CoreWebView2 Core;

        /// <summary>Last known address; survives the control being disposed.</summary>
        public string LastUrl = "";

        private readonly object _sync = new object();
        private readonly List<LogEntry> _logs = new List<LogEntry>();

        // requestId -> URL, so a failed request can be reported with its address
        // (Network.loadingFailed itself carries only the id).
        private readonly Dictionary<string, string> _requestUrls = new Dictionary<string, string>();

        // Event receivers must be held so they are not garbage collected.
        public readonly List<CoreWebView2DevToolsProtocolEventReceiver> Receivers =
            new List<CoreWebView2DevToolsProtocolEventReceiver>();

        public void Add(LogEntry e)
        {
            lock (_sync)
            {
                _logs.Add(e);
                if (_logs.Count > MaxLogEntries)
                    _logs.RemoveRange(0, _logs.Count - MaxLogEntries);
            }
        }

        public List<LogEntry> Snapshot()
        {
            lock (_sync) { return new List<LogEntry>(_logs); }
        }

        public void ClearLogs()
        {
            lock (_sync) { _logs.Clear(); }
        }

        public void TrackRequest(string requestId, string url)
        {
            if (string.IsNullOrEmpty(requestId)) return;
            lock (_sync)
            {
                if (_requestUrls.Count >= MaxTrackedRequests) _requestUrls.Clear();
                _requestUrls[requestId] = url;
            }
        }

        public string TakeRequestUrl(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return null;
            lock (_sync)
            {
                if (_requestUrls.TryGetValue(requestId, out string url))
                {
                    _requestUrls.Remove(requestId);
                    return url;
                }
            }
            return null;
        }

        public void ForgetRequests()
        {
            lock (_sync) { _requestUrls.Clear(); }
        }
    }
}
