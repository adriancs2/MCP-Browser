using MCP_Browser.Browser;
using MCP_Browser.Core;

namespace MCP_Browser.Tools
{
    /// <summary>
    /// Base class for all browser tools. The shared BrowserHost is injected by
    /// ToolDiscovery's factory (every tool has a (BrowserHost) constructor).
    /// </summary>
    public abstract class BrowserTool : ITool
    {
        protected readonly BrowserHost Host;

        protected BrowserTool(BrowserHost host)
        {
            Host = host;
        }

        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract ToolParamList Params { get; }
        public abstract ToolResult Execute(Newtonsoft.Json.Linq.JObject args);
    }
}
