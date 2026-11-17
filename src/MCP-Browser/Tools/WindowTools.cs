using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;

namespace MCP_Browser.Tools
{
    public class ShowWindowTool : BrowserTool
    {
        public ShowWindowTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_show_window";
        public override string Description =>
            "Bring the browser window to the front and focus it — restoring it if minimized, and reopening it if the user " +
            "closed it. Use this when the user should look at the page. (The window already opens and comes to the front " +
            "by itself the first time any browser tool is used, so this is rarely needed.)";

        public override ToolParamList Params => new ToolParamList();

        public override ToolResult Execute(JObject args)
        {
            return ToolResult.Success(Host.RunOnUi(() => Host.ShowWindowAsync()));
        }
    }

    public class HideWindowTool : BrowserTool
    {
        public HideWindowTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_hide_window";
        public override string Description =>
            "Minimize the browser window to the taskbar to get it out of the user's way. The page stays loaded and every " +
            "tool keeps working; browser_screenshot restores the window automatically (without stealing focus).";

        public override ToolParamList Params => new ToolParamList();

        public override ToolResult Execute(JObject args)
        {
            return ToolResult.Success(Host.MinimizeWindow());
        }
    }
}
