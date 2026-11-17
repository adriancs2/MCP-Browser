using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace MCP_Browser.Tools
{
    public class WaitTool : BrowserTool
    {
        public WaitTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_wait";

        public override string Description =>
            "Wait for a specified number of seconds (default: 1 second). Useful for giving the web browser room to finish " +
            "background API fetches, database operations, or DOM animations before performing the next step.";

        public override ToolParamList Params => new ToolParamList()
            .Int("seconds", "Number of seconds to wait. Default: 1 second.", defaultValue: 1);

        public override ToolResult Execute(JObject args)
        {
            int seconds = args.Value<int?>("seconds") ?? 1;
            if (seconds <= 0) seconds = 1;

            Thread.Sleep(seconds * 1000);
            return ToolResult.Success($"Waited {seconds} second(s).");
        }
    }
}
