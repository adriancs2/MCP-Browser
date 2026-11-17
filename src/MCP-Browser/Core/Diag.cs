using System;
using System.IO;

namespace MCP_Browser.Core
{
    /// <summary>
    /// Lightweight always-on-when-enabled diagnostic logger (separate from the
    /// JSON-RPC debug log) for tracing browser/UI-thread lifecycle.
    /// </summary>
    public static class Diag
    {
        private static readonly object Lock = new object();
        private static string _path;

        public static void Log(string message)
        {
            if (!McpConfig.DebugLogging) return;
            try
            {
                if (_path == null)
                    _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser_debug.log");
                lock (Lock)
                {
                    File.AppendAllText(_path,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [t{System.Threading.Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
