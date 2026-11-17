using MCP_Browser.Browser;
using MCP_Browser.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCP_Browser.Tools
{
    public class BatchTool : BrowserTool
    {
        public BatchTool(BrowserHost host) : base(host) { }

        public override string Name => "browser_batch";

        public override string Description =>
            "Execute a sequence of chained browser actions in a single call. Actions: navigate, back, forward, reload, click, " +
            "click_text, click_at, type, get_text, wait, wait_for_selector, scroll, execute_js, screenshot, clear_cache, " +
            "new_tab, switch_tab, close_tab. Elements can be targeted by CSS selector, by visible text (click_text), or — for " +
            "form fields — by visible label ('type' accepts 'label' instead of 'selector').\n" +
            "Execution STOPS at the first failed step by default (set continue_on_error=true only for genuinely independent " +
            "actions), and the result is returned with isError=true. The result is structured JSON — check 'ok' and 'failed_at' " +
            "rather than reading the prose. Screenshots taken in a batch are JPEG-compressed to keep token cost down. " +
            "Captured console and network errors for the current page are attached automatically.\n" +
            "A short settle delay (250 ms by default) is inserted between actions so the page can react before the next step; " +
            "set skip_delay=true to run the chain at full speed.";

        /// <summary>Outcome of a single batch step.</summary>
        private class StepOutcome
        {
            public bool Ok;
            public string Code;      // machine-readable failure code, null when Ok
            public string Detail;

            public static StepOutcome Good(string detail) { return new StepOutcome { Ok = true, Detail = detail }; }
            public static StepOutcome Bad(string code, string detail) { return new StepOutcome { Ok = false, Code = code, Detail = detail }; }
        }

        public override ToolParamList Params => new ToolParamList()
            .Array("actions", "JSON array of action objects executed in order. Each needs an 'action' property plus that action's arguments.", required: true)
            .Bool("continue_on_error", "Keep running after a step fails. Default: false — a chain of actions is aborted at the first failure, because later steps would act on an unexpected page state.", defaultValue: false)
            .Bool("auto_scroll", "Scroll elements into view before click or type. Default: true.", defaultValue: true)
            .Bool("skip_delay", "Skip the settle delay that is normally inserted after each action. Default: false — a 250 ms pause (configurable via batch_action_delay_ms) gives the page time to react before the next step.", defaultValue: false)
            .Bool("capture_console_errors", "Attach console errors/warnings captured during the batch. Default: true.", defaultValue: true)
            .Bool("capture_network_errors", "Attach failed requests and HTTP 4xx/5xx responses captured during the batch. Default: true.", defaultValue: true)
            .Int("screenshot_max_width", "Max width for screenshots taken in this batch. Default: from config (~1400).")
            .Int("screenshot_quality", "JPEG quality 1-100 for screenshots taken in this batch. Default: from config (80).");

        public override ToolResult Execute(JObject args)
        {
            var actionsArray = args["actions"] as JArray;
            if (actionsArray == null || actionsArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'actions' array parameter.");

            bool continueOnError = args.Value<bool?>("continue_on_error") ?? false;
            bool autoScroll = args.Value<bool?>("auto_scroll") ?? true;
            bool skipDelay = args.Value<bool?>("skip_delay") ?? false;
            int actionDelayMs = skipDelay ? 0 : Math.Max(0, McpConfig.BatchActionDelayMs);
            bool captureConsole = args.Value<bool?>("capture_console_errors") ?? true;
            bool captureNetwork = args.Value<bool?>("capture_network_errors") ?? true;
            int shotMaxWidth = args.Value<int?>("screenshot_max_width") ?? McpConfig.DefaultScreenshotMaxWidth;
            int shotQuality = args.Value<int?>("screenshot_quality") ?? McpConfig.BatchJpegQuality;

            // Start from a clean diagnostic window so attached errors belong to this batch.
            if (captureConsole || captureNetwork)
            {
                try { Host.ClearLogs(); } catch { }
            }

            var images = new List<ToolResultImage>();
            var steps = new JArray();
            int failedAt = -1;
            int stepsRun = 0;
            bool aborted = false;

            for (int i = 0; i < actionsArray.Count; i++)
            {
                int stepIndex = i + 1;
                var actObj = actionsArray[i] as JObject;
                string actionName = (actObj?.Value<string>("action") ?? "").ToLowerInvariant().Trim();

                StepOutcome outcome;
                if (actObj == null)
                {
                    actionName = "(invalid)";
                    outcome = StepOutcome.Bad("INVALID_ACTION", "Action entry is not a JSON object.");
                }
                else if (string.IsNullOrEmpty(actionName))
                {
                    actionName = "(missing)";
                    outcome = StepOutcome.Bad("INVALID_ACTION", "Action object has no 'action' property.");
                }
                else
                {
                    try
                    {
                        outcome = RunAction(actionName, actObj, stepIndex, autoScroll, shotMaxWidth, shotQuality, images);
                    }
                    catch (Exception ex)
                    {
                        outcome = StepOutcome.Bad("EXCEPTION", $"{ex.GetType().Name}: {ex.Message}");
                    }
                }

                stepsRun++;
                steps.Add(StepJson(stepIndex, actionName, outcome.Ok ? "ok" : "error", outcome.Code, outcome.Detail));

                if (!outcome.Ok)
                {
                    if (failedAt < 0) failedAt = stepIndex;
                    if (!continueOnError)
                    {
                        aborted = true;
                        for (int s = i + 1; s < actionsArray.Count; s++)
                        {
                            string skipped = (actionsArray[s] as JObject)?.Value<string>("action") ?? "(unknown)";
                            steps.Add(StepJson(s + 1, skipped, "skipped", null, "Not run: batch aborted at step " + stepIndex + "."));
                        }
                        break;
                    }
                }

                // Let the page react before the next step. Nothing follows the last
                // step, so a trailing pause would only add latency.
                if (actionDelayMs > 0 && i < actionsArray.Count - 1)
                    System.Threading.Thread.Sleep(actionDelayMs);
            }

            bool ok = failedAt < 0;

            var result = new JObject
            {
                ["ok"] = ok,
                ["steps_total"] = actionsArray.Count,
                ["steps_run"] = stepsRun,
                ["failed_at"] = failedAt < 0 ? (JToken)JValue.CreateNull() : failedAt,
                ["aborted"] = aborted,
                ["action_delay_ms"] = actionDelayMs,
                ["images_attached"] = images.Count,
                ["steps"] = steps
            };

            AttachDiagnostics(result, captureConsole, captureNetwork);

            if (!ok)
            {
                result["hint"] = aborted
                    ? $"Batch aborted at step {failedAt}; later steps did not run. Fix that step and re-issue the remainder."
                    : $"Step {failedAt} failed but continue_on_error was set, so later steps ran against a possibly unexpected page state.";
            }

            // Always return the payload as bare JSON with the error flag carried in
            // isError — ToolResult.Error would prefix the text and break parsing.
            return ToolResult.WithImages(result.ToString(Formatting.Indented), images, !ok);
        }

        private static JObject StepJson(int index, string action, string status, string code, string detail)
        {
            var o = new JObject
            {
                ["index"] = index,
                ["action"] = action,
                ["status"] = status
            };
            if (!string.IsNullOrEmpty(code)) o["code"] = code;
            if (detail != null) o["detail"] = detail;
            return o;
        }

        private void AttachDiagnostics(JObject result, bool captureConsole, bool captureNetwork)
        {
            if (!captureConsole && !captureNetwork) return;

            List<LogEntry> logs;
            try { logs = Host.GetLogs(); }
            catch { return; }

            if (captureConsole)
            {
                var consoleErrors = logs
                    .Where(e => e.Kind == "console" && (e.Level == "error" || e.Level == "warning"))
                    .Select(e => e.ToJson())
                    .ToArray();
                if (consoleErrors.Length > 0)
                    result["console_errors"] = new JArray(consoleErrors);
            }

            if (captureNetwork)
            {
                var netErrors = logs
                    .Where(e => e.Kind == "network")
                    .Select(e => e.ToJson())
                    .ToArray();
                if (netErrors.Length > 0)
                    result["network_errors"] = new JArray(netErrors);
            }
        }

        // ---- Individual actions ---------------------------------------------

        private StepOutcome RunAction(string action, JObject a, int stepIndex, bool autoScroll,
                                      int shotMaxWidth, int shotQuality, List<ToolResultImage> images)
        {
            switch (action)
            {
                case "wait":
                    {
                        int seconds = a.Value<int?>("seconds") ?? a.Value<int?>("duration_seconds") ?? 1;
                        if (seconds <= 0) seconds = 1;
                        System.Threading.Thread.Sleep(seconds * 1000);
                        return StepOutcome.Good($"Waited {seconds} second(s).");
                    }

                case "wait_for_selector":
                    {
                        string selector = a.Value<string>("selector");
                        if (string.IsNullOrWhiteSpace(selector))
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'selector'.");

                        int timeoutSec = a.Value<int?>("timeout_seconds") ?? a.Value<int?>("seconds") ?? 10;
                        int timeoutMs = a.Value<int?>("timeout_ms") ?? (timeoutSec * 1000);
                        bool found = Host.RunOnUi(() => Host.WaitForSelectorAsync(selector, timeoutMs));
                        return found
                            ? StepOutcome.Good($"Element '{selector}' appeared.")
                            : StepOutcome.Bad("TIMEOUT", $"Element '{selector}' did not appear within {timeoutMs} ms.");
                    }

                case "click":
                    {
                        string selector = a.Value<string>("selector");
                        if (string.IsNullOrWhiteSpace(selector))
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'selector'.");

                        string sel = JsonConvert.SerializeObject(selector);
                        string scrollJs = autoScroll ? "el.scrollIntoView({block:'center',inline:'center'});" : "";
                        string js = "(function(){var el=document.querySelector(" + sel + ");if(!el)return 'NOTFOUND';" + scrollJs + "el.click();return 'OK';})()";
                        string r = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));

                        return r == "\"NOTFOUND\""
                            ? StepOutcome.Bad("NOT_FOUND", $"No element matched '{selector}'.")
                            : StepOutcome.Good($"Clicked '{selector}'.");
                    }

                case "click_text":
                    {
                        string text = a.Value<string>("text");
                        if (string.IsNullOrWhiteSpace(text))
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'text'.");

                        bool exact = a.Value<bool?>("exact") ?? false;
                        bool clickableOnly = a.Value<bool?>("clickable_only") ?? true;
                        int index = a.Value<int?>("index") ?? -1;
                        string button = a.Value<string>("button") ?? "left";
                        int clickCount = a.Value<int?>("click_count") ?? 1;

                        try
                        {
                            string r = Host.RunOnUi(() => Host.ClickByTextAsync(text, exact, clickableOnly, index, button, clickCount));
                            return StepOutcome.Good(r);
                        }
                        catch (ArgumentException ex)
                        {
                            return StepOutcome.Bad("NOT_FOUND_OR_AMBIGUOUS", ex.Message);
                        }
                    }

                case "click_at":
                    {
                        if (a["x"] == null || a["y"] == null)
                            return StepOutcome.Bad("INVALID_PARAMS", "Both 'x' and 'y' are required.");

                        double x = a.Value<double>("x");
                        double y = a.Value<double>("y");
                        string button = a.Value<string>("button") ?? "left";
                        int clickCount = a.Value<int?>("click_count") ?? 1;

                        string r = Host.RunOnUi(() => Host.ClickAtAsync(x, y, button, clickCount));
                        return StepOutcome.Good(r);
                    }

                case "type":
                    {
                        string selector = a.Value<string>("selector");
                        string label = a.Value<string>("label");

                        if (string.IsNullOrWhiteSpace(selector))
                        {
                            if (string.IsNullOrWhiteSpace(label))
                                return StepOutcome.Bad("INVALID_PARAMS", "Missing target: provide 'selector' or 'label'.");

                            bool labelExact = a.Value<bool?>("exact") ?? false;
                            int labelIndex = a.Value<int?>("index") ?? -1;
                            try
                            {
                                selector = Host.RunOnUi(() => Host.ResolveFieldSelectorAsync(label, labelExact, labelIndex));
                            }
                            catch (ArgumentException ex)
                            {
                                return StepOutcome.Bad("NOT_FOUND_OR_AMBIGUOUS", ex.Message);
                            }
                        }

                        string text = a.Value<string>("text") ?? "";
                        bool clear = a.Value<bool?>("clear") ?? true;

                        string sel = JsonConvert.SerializeObject(selector);
                        string val = JsonConvert.SerializeObject(text);
                        string scrollJs = autoScroll ? "el.scrollIntoView({block:'center',inline:'center'});" : "";
                        string js =
                            "(function(){var el=document.querySelector(" + sel + ");if(!el)return 'NOTFOUND';" + scrollJs + "el.focus();" +
                            "if(el.isContentEditable){if(" + (clear ? "true" : "false") + ")el.textContent='';el.textContent+=" + val + ";}" +
                            "else{if(" + (clear ? "true" : "false") + ")el.value='';el.value=(el.value||'')+" + val + ";}" +
                            "el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));return 'OK';})()";

                        string r = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
                        return r == "\"NOTFOUND\""
                            ? StepOutcome.Bad("NOT_FOUND", $"No element matched '{selector}'.")
                            : StepOutcome.Good($"Typed into '{selector}'.");
                    }

                case "navigate":
                    {
                        string url = a.Value<string>("url");
                        if (string.IsNullOrWhiteSpace(url))
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'url'.");

                        int timeoutMs = a.Value<int?>("timeout_ms") ?? (a.Value<int?>("timeout_seconds") * 1000) ?? McpConfig.NavigationTimeoutMs;
                        string res = Host.RunOnUi(() => Host.NavigateAsync(UrlUtil.Normalize(url), timeoutMs));
                        return StepOutcome.Good(res);
                    }

                case "back":
                    {
                        int timeoutMs = a.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
                        return StepOutcome.Good(Host.RunOnUi(() => Host.GoBackAsync(timeoutMs)));
                    }

                case "forward":
                    {
                        int timeoutMs = a.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
                        return StepOutcome.Good(Host.RunOnUi(() => Host.GoForwardAsync(timeoutMs)));
                    }

                case "reload":
                    {
                        int timeoutMs = a.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
                        bool ignoreCache = a.Value<bool?>("ignore_cache") ?? false;
                        return StepOutcome.Good(Host.RunOnUi(() => Host.ReloadAsync(ignoreCache, timeoutMs)));
                    }

                case "scroll":
                    {
                        string dir = (a.Value<string>("direction") ?? "down").ToLowerInvariant();
                        int amount = a.Value<int?>("amount") ?? 600;
                        string res;
                        if (dir == "up") res = Host.RunOnUi(() => Host.ScrollByAsync(0, -Math.Abs(amount)));
                        else if (dir == "top") res = Host.RunOnUi(() => Host.ScrollToAsync(0, 0));
                        else if (dir == "bottom") res = Host.RunOnUi(() => Host.ExecuteScriptAsync("(function(){window.scrollTo(0,document.body.scrollHeight);return JSON.stringify({x:window.scrollX,y:window.scrollY});})()"));
                        else res = Host.RunOnUi(() => Host.ScrollByAsync(0, Math.Abs(amount)));
                        return StepOutcome.Good($"Scrolled {dir}. Position: {res}");
                    }

                case "execute_js":
                case "script":
                    {
                        string script = a.Value<string>("script") ?? a.Value<string>("js");
                        if (string.IsNullOrWhiteSpace(script))
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'script'.");

                        string res = Host.RunOnUi(() => Host.ExecuteScriptAsync(script));
                        return StepOutcome.Good("Result = " + res);
                    }

                case "get_text":
                    {
                        string selector = a.Value<string>("selector");
                        int maxChars = a.Value<int?>("max_chars") ?? 5000;

                        string js;
                        if (string.IsNullOrWhiteSpace(selector))
                            js = "(function(){return (document.body?document.body.innerText:'')||'';})()";
                        else
                            js = "(function(){var el=document.querySelector(" + JsonConvert.SerializeObject(selector) + ");return el?(el.innerText||el.textContent||''):'__NOTFOUND__';})()";

                        string raw = Host.RunOnUi(() => Host.ExecuteScriptAsync(js));
                        string decoded;
                        try { decoded = JsonConvert.DeserializeObject<string>(raw) ?? raw; }
                        catch { decoded = raw; }

                        if (decoded == "__NOTFOUND__")
                            return StepOutcome.Bad("NOT_FOUND", $"No element matched '{selector}'.");
                        if (decoded.Length > maxChars)
                            decoded = decoded.Substring(0, maxChars) + "\n... [truncated]";
                        return StepOutcome.Good(decoded);
                    }

                case "screenshot":
                    {
                        bool fullPage = a.Value<bool?>("full_page") ?? false;
                        string imgName = a.Value<string>("name") ?? $"step_{stepIndex}";
                        string format = a.Value<string>("format") ?? "jpg";
                        int maxWidth = a.Value<int?>("max_width") ?? shotMaxWidth;
                        int quality = a.Value<int?>("quality") ?? shotQuality;

                        byte[] pngBytes = Host.RunOnUi(() => Host.CaptureAsync(fullPage));
                        ImageUtil.Encoded enc = ImageUtil.Process(pngBytes, format, maxWidth, quality);

                        images.Add(new ToolResultImage
                        {
                            Data = Convert.ToBase64String(enc.Bytes),
                            MimeType = enc.MimeType
                        });

                        return StepOutcome.Good(
                            $"Screenshot '{imgName}' captured ({enc.MimeType}, {enc.Width}x{enc.Height}px, {enc.Bytes.Length / 1024.0:F1} KB).");
                    }

                case "clear_cache":
                    return StepOutcome.Good(Host.RunOnUi(() => Host.ClearCacheAsync()));

                case "new_tab":
                    {
                        string url = a.Value<string>("url");
                        int timeoutMs = a.Value<int?>("timeout_ms") ?? McpConfig.NavigationTimeoutMs;
                        return StepOutcome.Good(Host.RunOnUi(() => Host.NewTabAsync(string.IsNullOrWhiteSpace(url) ? null : UrlUtil.Normalize(url), timeoutMs)));
                    }

                case "switch_tab":
                    {
                        int index = a.Value<int?>("index") ?? -1;
                        if (index < 0)
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'index'.");
                        return StepOutcome.Good(Host.RunOnUi(() => Host.SwitchTabAsync(index)));
                    }

                case "close_tab":
                    {
                        int index = a.Value<int?>("index") ?? -1;
                        if (index < 0)
                            return StepOutcome.Bad("INVALID_PARAMS", "Missing 'index'.");
                        return StepOutcome.Good(Host.RunOnUi(() => Host.CloseTabAsync(index)));
                    }

                default:
                    return StepOutcome.Bad("UNKNOWN_ACTION", $"Unknown action '{action}'.");
            }
        }
    }
}
