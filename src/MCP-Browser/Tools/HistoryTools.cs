using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System;

namespace MCP_Browser.Tools
{
    public class BackTool : BrowserTool
    {
        public BackTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_back";
        public override string Description =>
            "Go back one entry in the active tab's history and wait for the page to load. " +
            "Use this to recover after following a wrong link, instead of re-navigating to the previous URL by hand.";

        public override ToolParamList Params => new ToolParamList()
            .Int("timeout_ms", "Max milliseconds to wait for the navigation. Default: configured navigation timeout.");

        public override ToolResult Execute(JObject args)
        {
            int timeout = args.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
            try
            {
                return ToolResult.Success(Host.RunOnUi(() => Host.GoBackAsync(timeout)));
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Error("NO_HISTORY", ex.Message);
            }
            catch (TimeoutException ex)
            {
                return ToolResult.Error("TIMEOUT", ex.Message);
            }
        }
    }

    public class ForwardTool : BrowserTool
    {
        public ForwardTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_forward";
        public override string Description =>
            "Go forward one entry in the active tab's history and wait for the page to load.";

        public override ToolParamList Params => new ToolParamList()
            .Int("timeout_ms", "Max milliseconds to wait for the navigation. Default: configured navigation timeout.");

        public override ToolResult Execute(JObject args)
        {
            int timeout = args.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
            try
            {
                return ToolResult.Success(Host.RunOnUi(() => Host.GoForwardAsync(timeout)));
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Error("NO_HISTORY", ex.Message);
            }
            catch (TimeoutException ex)
            {
                return ToolResult.Error("TIMEOUT", ex.Message);
            }
        }
    }

    public class ReloadTool : BrowserTool
    {
        public ReloadTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_reload";
        public override string Description =>
            "Reload the current page and wait for it to finish loading. Set ignore_cache=true for a hard reload that bypasses " +
            "the HTTP cache (useful after changing a local file) without wiping cookies the way browser_clear_cache does.";

        public override ToolParamList Params => new ToolParamList()
            .Bool("ignore_cache", "Bypass the HTTP cache for this reload. Default: false.", defaultValue: false)
            .Int("timeout_ms", "Max milliseconds to wait for the navigation. Default: configured navigation timeout.");

        public override ToolResult Execute(JObject args)
        {
            bool ignoreCache = args.Value<bool?>("ignore_cache") ?? false;
            int timeout = args.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
            try
            {
                return ToolResult.Success(Host.RunOnUi(() => Host.ReloadAsync(ignoreCache, timeout)));
            }
            catch (TimeoutException ex)
            {
                return ToolResult.Error("TIMEOUT", ex.Message);
            }
        }
    }
}
