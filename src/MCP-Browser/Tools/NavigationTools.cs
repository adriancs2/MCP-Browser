using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System;

namespace MCP_Browser.Tools
{
    public class NavigateTool : BrowserTool
    {
        public NavigateTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_navigate";
        public override string Description =>
            "Navigate the browser to a URL (a GET request). Accepts a bare host, an explicit scheme, and an optional port " +
            "(e.g. 'example.com', 'https://example.com/path', 'localhost:8080'). The browser session (cookies, storage) " +
            "persists across all tool calls. Returns the final URL, HTTP status, and page title.";

        public override ToolParamList Params => new ToolParamList()
            .String("url", "URL to navigate to. If no scheme is given, https:// is assumed (http:// for localhost / private IPs).", required: true)
            .Int("timeout_ms", "Max milliseconds to wait for navigation to complete. Default: configured navigation timeout.")
            .Bool("clear_cache", "When true, perform a full hard cache/site-data nuke (on-disk HTTP cache + in-memory cache + cookies + all site storage; logs out every site) BEFORE navigating. Saved passwords/autofill/history are kept. Default: false.", defaultValue: false);

        public override ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'url' parameter");

            url = UrlUtil.Normalize(url);

            int timeout = args.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
            bool clearCache = args.Value<bool?>("clear_cache") ?? false;
            string capturedUrl = url;
            string result = Host.RunOnUi(async () =>
            {
                string prefix = "";
                if (clearCache)
                    prefix = await Host.ClearCacheAsync() + "\n";
                return prefix + await Host.NavigateAsync(capturedUrl, timeout);
            });
            return ToolResult.Success(result);
        }
    }

    public class ClearCacheTool : BrowserTool
    {
        public ClearCacheTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_clear_cache";
        public override string Description =>
            "Full hard cache/site-data nuke: wipes the on-disk HTTP cache and ALL site data (cookies, local/session " +
            "storage, IndexedDB, Cache Storage), then drops the in-memory HTTP cache and cookies. This logs out every " +
            "site and forces all resources to be re-fetched. Saved passwords, autofill, history and settings are kept. " +
            "Use when you need a guaranteed-fresh browser state.";

        public override ToolParamList Params => new ToolParamList();

        public override ToolResult Execute(JObject args)
        {
            string result = Host.RunOnUi(() => Host.ClearCacheAsync());
            return ToolResult.Success(result);
        }
    }

    public class ScrollTool : BrowserTool
    {
        public ScrollTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_scroll";
        public override string Description =>
            "Scroll the current page. Use direction 'down'/'up' to scroll by a pixel amount, or 'top'/'bottom' to jump to the page edges. " +
            "Useful before taking another screenshot to reveal more content.";

        public override ToolParamList Params => new ToolParamList()
            .StringEnum("direction", "Scroll direction.", new[] { "down", "up", "top", "bottom" }, defaultValue: "down")
            .Int("amount", "Pixels to scroll for up/down. Default: 600.", defaultValue: 600);

        public override ToolResult Execute(JObject args)
        {
            string dir = (args.Value<string>("direction") ?? "down").ToLowerInvariant();
            int amount = args.Value<int?>("amount") ?? 600;

            string result;
            switch (dir)
            {
                case "up":
                    result = Host.RunOnUi(() => Host.ScrollByAsync(0, -Math.Abs(amount)));
                    break;
                case "top":
                    result = Host.RunOnUi(() => Host.ScrollToAsync(0, 0));
                    break;
                case "bottom":
                    result = Host.RunOnUi(() => Host.ExecuteScriptAsync(
                        "(function(){window.scrollTo(0,document.body.scrollHeight);return JSON.stringify({x:window.scrollX,y:window.scrollY});})()"));
                    break;
                default:
                    result = Host.RunOnUi(() => Host.ScrollByAsync(0, Math.Abs(amount)));
                    break;
            }
            return ToolResult.Success("Scrolled " + dir + ". Position: " + result);
        }
    }

    public class SetViewportTool : BrowserTool
    {
        public SetViewportTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_set_viewport";
        public override string Description =>
            "Set the browser viewport (resolution) in CSS pixels. Affects subsequent screenshots and page layout. " +
            "Smaller viewports produce smaller screenshots and cost fewer tokens.";

        public override ToolParamList Params => new ToolParamList()
            .Int("width", "Viewport width in pixels.", required: true)
            .Int("height", "Viewport height in pixels.", required: true);

        public override ToolResult Execute(JObject args)
        {
            int w = args.Value<int?>("width") ?? 0;
            int h = args.Value<int?>("height") ?? 0;
            if (w < 100 || h < 100 || w > 8000 || h > 8000)
                return ToolResult.Error("INVALID_PARAMS", "width/height must be between 100 and 8000.");

            string inner = Host.RunOnUi(() => Host.SetViewportAsync(w, h));
            return ToolResult.Success($"Viewport set to {w}x{h}. Inner size: {inner}");
        }
    }

    public class WaitForTool : BrowserTool
    {
        public WaitForTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_wait_for";
        public override string Description =>
            "Wait until a CSS selector appears in the page (polls until found or timeout). Use after an action that triggers async content loading.";

        public override ToolParamList Params => new ToolParamList()
            .String("selector", "CSS selector to wait for.", required: true)
            .Int("timeout_ms", "Max milliseconds to wait. Default: 10000.", defaultValue: 10000);

        public override ToolResult Execute(JObject args)
        {
            string selector = args.Value<string>("selector");
            if (string.IsNullOrWhiteSpace(selector))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'selector' parameter");
            int timeout = args.Value<int?>("timeout_ms") ?? 10000;

            bool found = Host.RunOnUi(() => Host.WaitForSelectorAsync(selector, timeout));
            return found
                ? ToolResult.Success($"Selector '{selector}' is present.")
                : ToolResult.Error("TIMEOUT", $"Selector '{selector}' did not appear within {timeout} ms.");
        }
    }
}
