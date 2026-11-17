using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System;

namespace MCP_Browser.Tools
{
    public class ListTabsTool : BrowserTool
    {
        public ListTabsTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_list_tabs";
        public override string Description =>
            "List the open tabs as JSON: index, id, url, title, and which one is active. " +
            "Use the 'index' from this list with browser_switch_tab and browser_close_tab. " +
            "All tabs share one browser session, so cookies and logins carry across them.";

        public override ToolParamList Params => new ToolParamList();

        public override ToolResult Execute(JObject args)
        {
            return ToolResult.Success(Host.RunOnUi(() => Host.ListTabsAsync()));
        }
    }

    public class NewTabTool : BrowserTool
    {
        public NewTabTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_new_tab";
        public override string Description =>
            "Open a new tab and make it active. Optionally navigate it to a URL in the same call. " +
            "The new tab shares the existing session (cookies, logins), so it is useful for comparing two pages " +
            "or keeping a reference page open while working in another.";

        public override ToolParamList Params => new ToolParamList()
            .String("url", "Optional URL to open in the new tab. If no scheme is given, https:// is assumed (http:// for localhost / private IPs).")
            .Int("timeout_ms", "Max milliseconds to wait for navigation. Default: configured navigation timeout.");

        public override ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            int timeout = args.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
            string normalized = string.IsNullOrWhiteSpace(url) ? null : UrlUtil.Normalize(url);

            return ToolResult.Success(Host.RunOnUi(() => Host.NewTabAsync(normalized, timeout)));
        }
    }

    public class SwitchTabTool : BrowserTool
    {
        public SwitchTabTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_switch_tab";
        public override string Description =>
            "Make another tab active. All subsequent tool calls (screenshot, click, get_text, ...) apply to the active tab. " +
            "Use browser_list_tabs to see the available indexes.";

        public override ToolParamList Params => new ToolParamList()
            .Int("index", "Zero-based tab index, as reported by browser_list_tabs.", required: true);

        public override ToolResult Execute(JObject args)
        {
            if (args["index"] == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'index' parameter");
            int index = args.Value<int>("index");

            try
            {
                return ToolResult.Success(Host.RunOnUi(() => Host.SwitchTabAsync(index)));
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Error("INVALID_PARAMS", ex.Message);
            }
        }
    }

    public class CloseTabTool : BrowserTool
    {
        public CloseTabTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_close_tab";
        public override string Description =>
            "Close a tab by index. The last remaining tab cannot be closed. " +
            "If the active tab is closed, the nearest remaining tab becomes active.";

        public override ToolParamList Params => new ToolParamList()
            .Int("index", "Zero-based tab index, as reported by browser_list_tabs.", required: true);

        public override ToolResult Execute(JObject args)
        {
            if (args["index"] == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'index' parameter");
            int index = args.Value<int>("index");

            try
            {
                return ToolResult.Success(Host.RunOnUi(() => Host.CloseTabAsync(index)));
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Error("INVALID_PARAMS", ex.Message);
            }
        }
    }
}
