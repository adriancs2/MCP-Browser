using System;
using System.Text.RegularExpressions;

namespace MCP_Browser.Core
{
    /// <summary>
    /// Turns what a person (or an AI) types into a navigable URL. One place for
    /// the rule, shared by the MCP tools and the address bar.
    /// </summary>
    public static class UrlUtil
    {
        private static readonly Regex SchemeOnly =
            new Regex(@"^(about|data|blob|javascript|view-source|edge|chrome):", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Private172 =
            new Regex(@"^172\.(1[6-9]|2\d|3[01])\.", RegexOptions.Compiled);

        /// <summary>
        /// Adds a scheme when none is given: http:// for localhost and private
        /// network addresses (which rarely serve TLS), https:// for everything else.
        /// </summary>
        public static string Normalize(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return url;

            if (url.IndexOf("://", StringComparison.Ordinal) >= 0 || SchemeOnly.IsMatch(url))
                return url;

            return (IsLocal(url) ? "http://" : "https://") + url;
        }

        private static bool IsLocal(string url)
        {
            // Host part only: strip path, then port.
            string host = url;
            int slash = host.IndexOf('/');
            if (slash >= 0) host = host.Substring(0, slash);

            if (host.StartsWith("[::1]", StringComparison.Ordinal)) return true;

            int colon = host.LastIndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);

            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("127.", StringComparison.Ordinal)
                || host.StartsWith("10.", StringComparison.Ordinal)
                || host.StartsWith("192.168.", StringComparison.Ordinal)
                || Private172.IsMatch(host);
        }

        /// <summary>True for pages worth reopening after the user closed the window.</summary>
        public static bool IsRestorable(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
