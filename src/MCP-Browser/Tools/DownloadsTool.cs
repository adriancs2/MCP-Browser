using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace MCP_Browser.Tools
{
    public class DownloadsTool : BrowserTool
    {
        public DownloadsTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_downloads";
        public override string Description =>
            "List the files the browser downloaded in this session, newest first: state (in_progress, completed, interrupted), " +
            "local file path, size and source URL. Downloads never show a Save dialog: they are saved automatically into the " +
            "download folder (" + McpConfig.DownloadFolder + "), and every tool result mentions downloads that started or " +
            "finished since the previous call. Use wait_seconds to wait for downloads that are still in progress.";

        public override ToolParamList Params => new ToolParamList()
            .Int("wait_seconds", "Wait up to this many seconds for in-progress downloads to finish before listing. Default: 0 (no wait). Max: 600.", defaultValue: 0)
            .Int("limit", "Maximum downloads to return. Default: 20.", defaultValue: 20);

        public override ToolResult Execute(JObject args)
        {
            int wait = Math.Max(0, Math.Min(600, args.Value<int?>("wait_seconds") ?? 0));
            int limit = Math.Max(1, args.Value<int?>("limit") ?? 20);

            bool allDone = wait == 0 || DownloadTracker.WaitForAll(TimeSpan.FromSeconds(wait));
            var list = DownloadTracker.List();
            DownloadTracker.TakeNotices();   // this listing already tells the agent everything

            var result = new JObject
            {
                ["download_folder"] = McpConfig.DownloadFolder,
                ["count"] = list.Count,
                ["downloads"] = new JArray(list.AsEnumerable().Reverse().Take(limit).Select(d => d.ToJson()).ToArray())
            };
            if (!allDone)
                result["note"] = $"Still in progress after {wait} s. Call again with wait_seconds to keep waiting.";
            if (list.Count == 0)
                result["note"] = "Nothing has been downloaded in this session.";
            return ToolResult.Success(result.ToString(Formatting.Indented));
        }
    }
}
