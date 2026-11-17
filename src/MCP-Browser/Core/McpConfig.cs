using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MCP_Browser.Core
{
    /// <summary>
    /// Configuration loader for the browser MCP server. Reads mcp-config.json
    /// next to the executable; writes a default sample on first run. Every key
    /// is optional — a missing or malformed value falls back to its default.
    /// </summary>
    public static class McpConfig
    {
        /// <summary>Initial page viewport size (the window is sized around it).</summary>
        public static int WindowWidth { get; private set; } = 1350;
        public static int WindowHeight { get; private set; } = 700;

        /// <summary>
        /// When false (default) the window is brought to the front only when it is
        /// opened / reopened or browser_show_window is called, so the user can push
        /// it aside or minimize it while the AI keeps working. When true, every
        /// tool call that touches the browser raises and focuses the window.
        /// </summary>
        public static bool BringToFrontOnEveryCall { get; private set; } = false;

        /// <summary>Reopen the pages that were open when the user closed the window.</summary>
        public static bool RestoreTabsOnReopen { get; private set; } = true;

        /// <summary>
        /// Auto-accept alert/confirm/prompt/beforeunload dialogs (and record them in
        /// the console log). A native dialog blocks the page's script thread, which
        /// would stall the AI until a human clicks it.
        /// </summary>
        public static bool AutoAcceptDialogs { get; private set; } = true;

        public static int DefaultScreenshotMaxWidth { get; private set; } = 1400;
        public static string DefaultScreenshotFormat { get; private set; } = "jpg";
        public static int DefaultJpegQuality { get; private set; } = 70;
        public static int BatchJpegQuality { get; private set; } = 80;
        public static int BatchActionDelayMs { get; private set; } = 250;
        public static int NavigationTimeoutMs { get; private set; } = 30000;
        public static string UserDataFolder { get; private set; }

        /// <summary>
        /// Where downloads are saved, automatically and without a Save dialog.
        /// Default: %USERPROFILE%\Downloads\McpBrowser.
        /// </summary>
        public static string DownloadFolder { get; private set; }
        public static bool DebugLogging { get; private set; }

        public static string ConfigFilePath { get; private set; }

        private const string DefaultConfigJson = @"{
  ""window_width"": 1350,
  ""window_height"": 700,
  ""bring_to_front_on_every_call"": false,
  ""restore_tabs_on_reopen"": true,
  ""auto_accept_dialogs"": true,
  ""default_screenshot_max_width"": 1400,
  ""default_screenshot_format"": ""jpg"",
  ""default_jpeg_quality"": 70,
  ""batch_jpeg_quality"": 80,
  ""batch_action_delay_ms"": 250,
  ""navigation_timeout_ms"": 30000,
  ""user_data_folder"": null,
  ""download_folder"": null,
  ""debug_logging"": false
}";

        public static void Load()
        {
            ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-config.json");

            JObject config = null;
            try
            {
                if (!File.Exists(ConfigFilePath))
                    File.WriteAllText(ConfigFilePath, DefaultConfigJson, new System.Text.UTF8Encoding(false));
                config = JObject.Parse(File.ReadAllText(ConfigFilePath));
            }
            catch
            {
                // Read-only install folder or malformed JSON: run on defaults.
            }

            if (config != null)
            {
                WindowWidth = Clamp(Int(config, "window_width", WindowWidth), 200, 8000);
                WindowHeight = Clamp(Int(config, "window_height", WindowHeight), 200, 8000);
                BringToFrontOnEveryCall = Bool(config, "bring_to_front_on_every_call", BringToFrontOnEveryCall);
                RestoreTabsOnReopen = Bool(config, "restore_tabs_on_reopen", RestoreTabsOnReopen);
                AutoAcceptDialogs = Bool(config, "auto_accept_dialogs", AutoAcceptDialogs);
                DefaultScreenshotMaxWidth = Int(config, "default_screenshot_max_width", DefaultScreenshotMaxWidth);
                DefaultScreenshotFormat = Str(config, "default_screenshot_format", DefaultScreenshotFormat);
                DefaultJpegQuality = Int(config, "default_jpeg_quality", DefaultJpegQuality);
                BatchJpegQuality = Int(config, "batch_jpeg_quality", BatchJpegQuality);
                BatchActionDelayMs = Int(config, "batch_action_delay_ms", BatchActionDelayMs);
                NavigationTimeoutMs = Int(config, "navigation_timeout_ms", NavigationTimeoutMs);
                UserDataFolder = Str(config, "user_data_folder", null);
                DownloadFolder = Str(config, "download_folder", null);
                DebugLogging = Bool(config, "debug_logging", false);
            }

            if (string.IsNullOrWhiteSpace(UserDataFolder))
            {
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "McpBrowser", "WebView2Data");
            }

            if (string.IsNullOrWhiteSpace(DownloadFolder))
            {
                DownloadFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "McpBrowser");
            }
            else
            {
                DownloadFolder = Environment.ExpandEnvironmentVariables(DownloadFolder);
            }
        }

        // Each value is read on its own so one bad entry cannot discard the rest.

        private static int Int(JObject o, string key, int fallback)
        {
            try
            {
                var t = o[key];
                return t == null || t.Type == JTokenType.Null ? fallback : t.Value<int>();
            }
            catch { return fallback; }
        }

        private static bool Bool(JObject o, string key, bool fallback)
        {
            try
            {
                var t = o[key];
                return t == null || t.Type == JTokenType.Null ? fallback : t.Value<bool>();
            }
            catch { return fallback; }
        }

        private static string Str(JObject o, string key, string fallback)
        {
            try
            {
                var t = o[key];
                return t == null || t.Type == JTokenType.Null ? fallback : t.Value<string>();
            }
            catch { return fallback; }
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
