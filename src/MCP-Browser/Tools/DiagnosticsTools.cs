using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace MCP_Browser.Tools
{
    public class GetConsoleTool : BrowserTool
    {
        public GetConsoleTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_get_console";
        public override string Description =>
            "Read the console messages and failed network requests captured for the current page — the fastest way to find out " +
            "why a page is misbehaving, without a screenshot. Covers console.* calls, uncaught exceptions and unhandled promise " +
            "rejections, plus HTTP 4xx/5xx responses and requests that failed outright. " +
            "The buffer is reset automatically on each top-level navigation, so it always describes the page you are on.";

        public override ToolParamList Params => new ToolParamList()
            .StringEnum("kind", "Which entries to return.", new[] { "all", "console", "network" }, defaultValue: "all")
            .StringEnum("level", "Minimum severity. 'error' returns only errors; 'warning' returns warnings and errors; 'all' returns everything including logs.", new[] { "all", "warning", "error" }, defaultValue: "all")
            .Int("limit", "Maximum entries to return, newest kept. Default: 100.", defaultValue: 100)
            .Bool("clear", "Clear the buffer after reading. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string kind = args.Value<string>("kind") ?? "all";
            string level = args.Value<string>("level") ?? "all";
            int limit = args.Value<int?>("limit") ?? 100;
            bool clear = args.Value<bool?>("clear") ?? false;

            var logs = Host.GetLogs(kind, level == "all" ? null : level);

            if (limit > 0 && logs.Count > limit)
                logs = logs.Skip(logs.Count - limit).ToList();

            if (clear) Host.ClearLogs();

            if (logs.Count == 0)
                return ToolResult.Success($"No {(level == "all" ? "" : level + " ")}entries captured for the current page (kind={kind}).");

            var arr = new JArray(logs.Select(e => e.ToJson()).ToArray());
            var result = new JObject
            {
                ["count"] = logs.Count,
                ["url"] = Host.CurrentUrl ?? "",
                ["entries"] = arr
            };
            return ToolResult.Success(result.ToString(Formatting.Indented));
        }
    }
}
