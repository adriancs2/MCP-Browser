using MCP_Browser.Core;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MCP_Browser.Browser
{
    /// <summary>One file the browser downloaded (or is downloading).</summary>
    public class DownloadRecord
    {
        public int Id;
        public string Url;
        public string FileName;
        public string Path;
        public string MimeType;
        public string State = "in_progress";    // in_progress | completed | interrupted
        public string Reason;                    // why it was interrupted
        public long BytesReceived;
        public long? TotalBytes;
        public DateTime StartedUtc;
        public DateTime? FinishedUtc;

        public JObject ToJson()
        {
            var o = new JObject
            {
                ["id"] = Id,
                ["state"] = State,
                ["file_name"] = FileName,
                ["path"] = Path,
                ["url"] = Url,
                ["bytes"] = BytesReceived,
                ["started"] = StartedUtc.ToLocalTime().ToString("HH:mm:ss")
            };
            if (TotalBytes.HasValue) o["total_bytes"] = TotalBytes.Value;
            if (!string.IsNullOrEmpty(MimeType)) o["mime_type"] = MimeType;
            if (!string.IsNullOrEmpty(Reason)) o["reason"] = Reason;
            if (FinishedUtc.HasValue) o["finished"] = FinishedUtc.Value.ToLocalTime().ToString("HH:mm:ss");
            return o;
        }

        public string Describe()
        {
            switch (State)
            {
                case "completed": return $"Download #{Id} finished: {Path} ({BytesReceived:N0} bytes)";
                case "interrupted": return $"Download #{Id} failed ({Reason}): {FileName} from {Url}";
                default: return $"Download #{Id} started: {FileName} -> {Path}. Call browser_downloads with wait_seconds to wait for it.";
            }
        }
    }

    /// <summary>
    /// Saves every download automatically into McpConfig.DownloadFolder, without the
    /// browser's Save dialog or download panel (nobody may be watching the window), and
    /// remembers what happened so the agent can be told: each tool result carries a
    /// note about downloads that started or finished since the previous call.
    /// </summary>
    public static class DownloadTracker
    {
        private const int MaxRecords = 200;
        private static readonly object Sync = new object();
        private static readonly List<DownloadRecord> Records = new List<DownloadRecord>();
        private static readonly List<string> Notices = new List<string>();
        private static int _nextId = 1;

        /// <summary>Wire a tab's WebView2 to the tracker. Call once per tab, on the UI thread.</summary>
        internal static void Attach(CoreWebView2 core)
        {
            core.DownloadStarting += OnDownloadStarting;
            core.PermissionRequested += OnPermissionRequested;
        }

        // Chromium lets a page start one download on its own; any further automatic
        // download needs the "multiple automatic downloads" permission, which WebView2
        // otherwise denies without a word. The agent asked for those files, so allow it.
        private static void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.MultipleAutomaticDownloads)
            {
                e.State = CoreWebView2PermissionState.Allow;
                return;
            }
            // Anything else (camera, location, notifications...) keeps WebView2's default
            // handling, but the agent is told it happened.
            lock (Sync)
                Notices.Add($"The page asked for the '{e.PermissionKind}' permission ({e.Uri}). " +
                            "It was not granted automatically; a person may need to answer it in the browser window.");
        }

        private static void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            CoreWebView2DownloadOperation op = e.DownloadOperation;
            var rec = new DownloadRecord
            {
                Url = op.Uri,
                MimeType = op.MimeType,
                TotalBytes = op.TotalBytesToReceive.HasValue ? (long?)(long)op.TotalBytesToReceive.Value : null,
                StartedUtc = DateTime.UtcNow
            };

            try
            {
                string folder = McpConfig.DownloadFolder;
                Directory.CreateDirectory(folder);
                // WebView2 proposes a full path in the user's Downloads folder: keep only its name.
                string name = SafeFileName(System.IO.Path.GetFileName(e.ResultFilePath));
                rec.Path = UniquePath(folder, name);
                rec.FileName = System.IO.Path.GetFileName(rec.Path);
                e.ResultFilePath = rec.Path;
            }
            catch (Exception ex)
            {
                // Folder not writable: fall back to WebView2's own choice, still silently.
                Diag.Log("Download folder unusable: " + ex.Message);
                rec.Path = e.ResultFilePath;
                rec.FileName = System.IO.Path.GetFileName(rec.Path);
            }

            e.Handled = true;   // no Save As dialog, no download panel

            lock (Sync)
            {
                rec.Id = _nextId++;
                Records.Add(rec);
                if (Records.Count > MaxRecords) Records.RemoveAt(0);
                Notices.Add(rec.Describe());
            }

            op.BytesReceivedChanged += (s, a) => { lock (Sync) rec.BytesReceived = op.BytesReceived; };
            op.StateChanged += (s, a) => OnStateChanged(rec, op);
        }

        private static void OnStateChanged(DownloadRecord rec, CoreWebView2DownloadOperation op)
        {
            lock (Sync)
            {
                rec.BytesReceived = op.BytesReceived;
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    rec.State = "completed";
                    rec.Path = op.ResultFilePath;
                }
                else if (op.State == CoreWebView2DownloadState.Interrupted)
                {
                    rec.State = "interrupted";
                    rec.Reason = op.InterruptReason.ToString();
                }
                else return;

                rec.FinishedUtc = DateTime.UtcNow;
                Notices.Add(rec.Describe());
                Monitor.PulseAll(Sync);
            }
        }

        /// <summary>Notes about downloads since the last call (empty when nothing happened).</summary>
        public static List<string> TakeNotices()
        {
            lock (Sync)
            {
                var list = new List<string>(Notices);
                Notices.Clear();
                return list;
            }
        }

        public static List<DownloadRecord> List()
        {
            lock (Sync) return new List<DownloadRecord>(Records);
        }

        /// <summary>Blocks until no download is in progress, or the timeout passes. Returns true when all are done.</summary>
        public static bool WaitForAll(TimeSpan timeout)
        {
            DateTime end = DateTime.UtcNow + timeout;
            lock (Sync)
            {
                while (Records.Any(r => r.State == "in_progress"))
                {
                    TimeSpan left = end - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return false;
                    Monitor.Wait(Sync, left);
                }
                return true;
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string UniquePath(string folder, string name)
        {
            string path = System.IO.Path.Combine(folder, name);
            string stem = System.IO.Path.GetFileNameWithoutExtension(name);
            string ext = System.IO.Path.GetExtension(name);
            for (int i = 1; File.Exists(path) || IsReserved(path); i++)
                path = System.IO.Path.Combine(folder, $"{stem} ({i}){ext}");
            return path;
        }

        // A download that has not been written yet still owns its name.
        private static bool IsReserved(string path)
        {
            lock (Sync)
                return Records.Any(r => r.State == "in_progress" &&
                                        string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
