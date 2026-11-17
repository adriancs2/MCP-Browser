using MCP_Browser.Browser;
using MCP_Browser.Core;
using System;
using System.IO;
using System.Text;

namespace MCP_Browser
{
    /// <summary>
    /// MCP Browser for AI — a console MCP server (JSON-RPC over stdin/stdout) that
    /// drives a WebView2 browser window.
    ///
    /// The two halves are deliberately detached:
    ///  - this main thread owns the MCP transport and lives as long as the client
    ///    keeps stdin open;
    ///  - the browser window lives on its own UI thread, is NOT created at startup,
    ///    appears the first time a tool needs it, and can be closed by the user at
    ///    any time without ending the process (the next tool call reopens it).
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            // stdin/stdout ARE the MCP transport. Force UTF-8 (no BOM) on the pipes
            // regardless of the host's console code page.
            var utf8 = new UTF8Encoding(false);
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
            }
            catch { /* handles may not be redirectable when launched standalone */ }

            McpConfig.Load();

            var host = new BrowserHost();
            host.Start();               // starts the UI thread only — no window yet

            var server = new McpServer(host);
            server.Run();               // blocks until the client closes stdin

            host.Shutdown();
            Environment.Exit(0);
        }
    }
}
