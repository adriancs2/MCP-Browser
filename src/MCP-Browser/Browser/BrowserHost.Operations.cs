using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MCP_Browser.Browser
{
    // The page operations the MCP tools call. Every method here runs on the UI
    // thread, inside BrowserHost.RunOnUi(...), against the active tab.
    public partial class BrowserHost
    {
        // ---- Tabs (tool-facing) ----------------------------------------------

        public async Task<string> NewTabAsync(string url, int timeoutMs)
        {
            BrowserTab tab = await AddTabAsync();
            int index = _tabs.IndexOf(tab);
            SetActiveTab(index);

            if (!string.IsNullOrWhiteSpace(url))
            {
                string nav = await NavigateAsync(url, timeoutMs);
                return $"Opened tab {tab.Id} (index {index}) and activated it.\n{nav}";
            }
            return $"Opened blank tab {tab.Id} (index {index}) and activated it.";
        }

        public Task<string> SwitchTabAsync(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                throw new ArgumentException($"Tab index {index} is out of range (0..{_tabs.Count - 1}).");
            SetActiveTab(index);
            return Task.FromResult($"Switched to tab index {index} (id {ActiveTab.Id}): {ActiveTab.Core?.Source}");
        }

        public Task<string> CloseTabAsync(int index)
        {
            if (_tabs.Count <= 1)
                throw new ArgumentException("Cannot close the last remaining tab.");
            if (index < 0 || index >= _tabs.Count)
                throw new ArgumentException($"Tab index {index} is out of range (0..{_tabs.Count - 1}).");

            int closedId = _tabs[index].Id;
            RemoveTab(index);
            return Task.FromResult($"Closed tab id {closedId}. Active tab is now index {_activeIndex} (id {ActiveTab.Id}).");
        }

        public Task<string> ListTabsAsync()
        {
            var arr = new JArray();
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["id"] = t.Id,
                    ["active"] = (i == _activeIndex),
                    ["url"] = t.Core?.Source ?? "",
                    ["title"] = t.Core?.DocumentTitle ?? ""
                });
            }
            return Task.FromResult(arr.ToString(Formatting.Indented));
        }

        // ---- Navigation ------------------------------------------------------

        private class NavOutcome
        {
            public bool Success;
            public int HttpStatus;
            public bool SameDocument;
            public CoreWebView2WebErrorStatus Error;
        }

        /// <summary>
        /// Starts a navigation and waits for THAT navigation to finish.
        ///
        /// NavigationCompleted alone is not a reliable signal: if another load was
        /// still in flight, its cancellation completes first and would be mistaken
        /// for ours; and same-document moves (fragment links, history entries made
        /// with pushState) may complete without a new document at all. So the
        /// navigation id announced by NavigationStarting is matched, cancellations
        /// of superseded loads are ignored, and a same-document SourceChanged is
        /// accepted as completion when no real navigation follows it.
        /// </summary>
        private async Task<NavOutcome> RunNavigationAsync(CoreWebView2 core, Action start, string what, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<NavOutcome>();
            ulong? navId = null;

            EventHandler<CoreWebView2NavigationStartingEventArgs> onStarting = (s, e) => navId = e.NavigationId;

            EventHandler<CoreWebView2NavigationCompletedEventArgs> onCompleted = (s, e) =>
            {
                if (navId.HasValue)
                {
                    if (e.NavigationId != navId.Value) return;      // an older, superseded load
                }
                else if (!e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    return;                                         // the load we just interrupted
                }

                int status = 0;
                try { status = e.HttpStatusCode; } catch { }
                tcs.TrySetResult(new NavOutcome { Success = e.IsSuccess, HttpStatus = status, Error = e.WebErrorStatus });
            };

            EventHandler<CoreWebView2SourceChangedEventArgs> onSourceChanged = async (s, e) =>
            {
                if (e.IsNewDocument) return;
                await Task.Delay(400);
                if (!navId.HasValue)
                    tcs.TrySetResult(new NavOutcome { Success = true, SameDocument = true });
            };

            core.NavigationStarting += onStarting;
            core.NavigationCompleted += onCompleted;
            core.SourceChanged += onSourceChanged;
            try
            {
                try
                {
                    start();
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"{what} failed: {ex.Message}");
                }

                if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) != tcs.Task)
                    throw new TimeoutException($"{what} did not complete within {timeoutMs} ms. The page may still be loading; " +
                                               "use browser_wait_for or take a screenshot to check.");
                return tcs.Task.Result;
            }
            finally
            {
                try
                {
                    core.NavigationStarting -= onStarting;
                    core.NavigationCompleted -= onCompleted;
                    core.SourceChanged -= onSourceChanged;
                }
                catch { /* the tab may be gone */ }
            }
        }

        private static string DescribeNavigation(string lead, CoreWebView2 core, NavOutcome o)
        {
            string text = $"{lead} {core.Source}\nHTTP status: {o.HttpStatus}\nSuccess: {o.Success}\nTitle: {core.DocumentTitle}";
            if (o.SameDocument)
                text += "\nNote: same-document navigation (no page load).";
            else if (!o.Success && o.Error != CoreWebView2WebErrorStatus.Unknown)
                text += "\nError: " + o.Error;
            return text;
        }

        public async Task<string> NavigateAsync(string url, int timeoutMs)
        {
            CoreWebView2 core = _core;
            NavOutcome o = await RunNavigationAsync(core, () => core.Navigate(url), $"Navigation to '{url}'", timeoutMs);
            return DescribeNavigation("Navigated to", core, o);
        }

        public async Task<string> GoBackAsync(int timeoutMs)
        {
            CoreWebView2 core = _core;
            if (!core.CanGoBack)
                throw new ArgumentException("No previous page in this tab's history.");
            NavOutcome o = await RunNavigationAsync(core, () => core.GoBack(), "back", timeoutMs);
            return DescribeNavigation("Went back. Now at", core, o);
        }

        public async Task<string> GoForwardAsync(int timeoutMs)
        {
            CoreWebView2 core = _core;
            if (!core.CanGoForward)
                throw new ArgumentException("No forward page in this tab's history.");
            NavOutcome o = await RunNavigationAsync(core, () => core.GoForward(), "forward", timeoutMs);
            return DescribeNavigation("Went forward. Now at", core, o);
        }

        public async Task<string> ReloadAsync(bool ignoreCache, int timeoutMs)
        {
            CoreWebView2 core = _core;
            if (ignoreCache)
            {
                NavOutcome hard = await RunNavigationAsync(core,
                    () => { var _ = core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}"); },
                    "reload (cache bypassed)", timeoutMs);
                return DescribeNavigation("Reloaded (cache bypassed). Now at", core, hard);
            }
            NavOutcome o = await RunNavigationAsync(core, () => core.Reload(), "reload", timeoutMs);
            return DescribeNavigation("Reloaded. Now at", core, o);
        }

        /// <summary>
        /// Performs a full hard clear ("nuke"): wipes the on-disk HTTP cache and
        /// ALL site data (cookies, local/session storage, IndexedDB, Cache
        /// Storage, FileSystems, WebSQL) via the WebView2 profile, then drops the
        /// in-memory HTTP cache and cookies via CDP. This logs out every site and
        /// forces all resources to be re-fetched. Saved passwords, autofill,
        /// browsing history and browser settings are NOT touched.
        /// </summary>
        public async Task<string> ClearCacheAsync()
        {
            string profileResult;
            try
            {
                var profile = _core.Profile;
                if (profile != null)
                {
                    await profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.DiskCache |
                        CoreWebView2BrowsingDataKinds.AllSite);
                    profileResult = "disk cache + all site data (cookies/storage/IndexedDB/cache storage) cleared";
                }
                else
                {
                    profileResult = "profile unavailable; skipped profile clear";
                }
            }
            catch (Exception ex)
            {
                profileResult = "profile clear failed: " + ex.Message;
            }

            string cdpResult;
            try
            {
                await _core.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
                await _core.CallDevToolsProtocolMethodAsync("Network.clearBrowserCookies", "{}");
                cdpResult = "in-memory HTTP cache + cookies cleared";
            }
            catch (Exception ex)
            {
                cdpResult = "CDP clear failed: " + ex.Message;
            }

            return $"Full cache/site-data nuke: {profileResult}; {cdpResult}.";
        }

        // ---- Script ----------------------------------------------------------

        public Task<string> ExecuteScriptAsync(string js)
        {
            return _core.ExecuteScriptAsync(js);
        }

        /// <summary>Evaluates an expression via CDP, awaiting any returned Promise.</summary>
        public async Task<string> CdpEvaluateAsync(string expression, bool awaitPromise)
        {
            var p = new JObject
            {
                ["expression"] = expression,
                ["awaitPromise"] = awaitPromise,
                ["returnByValue"] = true,
                ["userGesture"] = true
            };
            return await _core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p.ToString(Formatting.None));
        }

        // ---- Screenshot ------------------------------------------------------

        /// <summary>
        /// PNG screenshot. A minimized window cannot render, so it is restored first
        /// — without activating it, so the user's current window keeps the focus.
        ///
        /// fullPage: the whole document, captured from the top with an explicit clip
        /// of the real content size. (captureBeyondViewport alone renders at the
        /// current scroll position: fixed / sticky elements land mid-image over the
        /// content, and the image size varies between captures.) The scroll position
        /// is restored afterwards.
        /// </summary>
        public async Task<byte[]> CaptureAsync(bool fullPage)
        {
            if (NativeMethods.RestoreIfMinimized(_form))
                await Task.Delay(400);      // let the page lay out and paint at its real size

            CoreWebView2 core = _core;
            var p = new JObject { ["format"] = "png" };
            string savedScroll = null;
            try
            {
                if (fullPage)
                {
                    // 1. from the top, so fixed elements sit where they belong (once, at the top)
                    savedScroll = await core.ExecuteScriptAsync("(function(){var s=JSON.stringify({x:window.scrollX,y:window.scrollY});window.scrollTo(0,0);return s;})()");

                    // 2. the real content size (CSS pixels)
                    string metrics = await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}");
                    var m = JObject.Parse(metrics);
                    var size = m["cssContentSize"] ?? m["contentSize"];
                    double w = size?["width"]?.Value<double>() ?? 0;
                    double h = size?["height"]?.Value<double>() ?? 0;

                    // 3. capture exactly that area
                    p["captureBeyondViewport"] = true;
                    if (w > 0 && h > 0)
                        p["clip"] = new JObject { ["x"] = 0, ["y"] = 0, ["width"] = Math.Ceiling(w), ["height"] = Math.Ceiling(h), ["scale"] = 1 };
                }

                string json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", p.ToString(Formatting.None));
                var data = JObject.Parse(json)["data"]?.Value<string>();
                if (string.IsNullOrEmpty(data))
                    throw new InvalidOperationException("Page.captureScreenshot returned no data.");
                return Convert.FromBase64String(data);
            }
            finally
            {
                // 4. put the page back where it was (even when the capture failed)
                if (savedScroll != null)
                {
                    try
                    {
                        string js = JsonConvert.DeserializeObject<string>(savedScroll) ?? savedScroll;
                        var s = JObject.Parse(js);
                        var inv = CultureInfo.InvariantCulture;
                        double sx = s["x"]?.Value<double>() ?? 0, sy = s["y"]?.Value<double>() ?? 0;
                        await core.ExecuteScriptAsync("window.scrollTo(" + sx.ToString(inv) + "," + sy.ToString(inv) + ")");
                    }
                    catch { }
                }
            }
        }

        // ---- Scrolling / viewport --------------------------------------------

        public Task<string> ScrollByAsync(int dx, int dy)
        {
            return _core.ExecuteScriptAsync($"(function(){{window.scrollBy({dx},{dy});return JSON.stringify({{x:window.scrollX,y:window.scrollY,maxY:document.body?document.body.scrollHeight:0}});}})()");
        }

        public Task<string> ScrollToAsync(int x, int y)
        {
            return _core.ExecuteScriptAsync($"(function(){{window.scrollTo({x},{y});return JSON.stringify({{x:window.scrollX,y:window.scrollY}});}})()");
        }

        /// <summary>
        /// Resizes the window so the page viewport is width x height. The window is
        /// taken out of the minimized / maximized state if necessary. Windows caps a
        /// window at roughly the desktop size, so the returned inner size is the
        /// truth — it can be smaller than requested on a small screen.
        /// </summary>
        public async Task<string> SetViewportAsync(int width, int height)
        {
            _form.SetViewportSize(width, height);
            await Task.Delay(150);          // let the resize reach the page
            return await _core.ExecuteScriptAsync("JSON.stringify({innerWidth:window.innerWidth,innerHeight:window.innerHeight})");
        }

        public Task<double> SetZoomAsync(double scale)
        {
            _webView.ZoomFactor = scale;
            return Task.FromResult(_webView.ZoomFactor);
        }

        /// <summary>Returns JSON with the current layout viewport size, for mapping screenshot pixels back to click coordinates.</summary>
        public Task<string> GetViewportInfoAsync()
        {
            return _core.ExecuteScriptAsync(
                "JSON.stringify({innerWidth:window.innerWidth,innerHeight:window.innerHeight,devicePixelRatio:window.devicePixelRatio,scrollX:window.scrollX,scrollY:window.scrollY})");
        }

        // ---- Forms / waiting -------------------------------------------------

        public async Task<string> SetFileInputAsync(string selector, string filePath)
        {
            string docJson = await _core.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":0}");
            int rootNodeId = JObject.Parse(docJson)["root"]["nodeId"].Value<int>();

            var query = new JObject { ["nodeId"] = rootNodeId, ["selector"] = selector };
            string qJson = await _core.CallDevToolsProtocolMethodAsync("DOM.querySelector", query.ToString(Formatting.None));
            int nodeId = JObject.Parse(qJson)["nodeId"].Value<int>();
            if (nodeId == 0)
                throw new ArgumentException($"No element matched selector '{selector}'.");

            var setFiles = new JObject
            {
                ["files"] = new JArray { filePath },
                ["nodeId"] = nodeId
            };
            await _core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", setFiles.ToString(Formatting.None));
            return $"Set file input '{selector}' to: {filePath}";
        }

        public async Task<bool> WaitForSelectorAsync(string selector, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string js = "(function(){return document.querySelector(" + JsonConvert.SerializeObject(selector) + ")?true:false;})()";
            do
            {
                // _core is re-read each round: the page may open the element in a new tab.
                string r = await _core.ExecuteScriptAsync(js);
                if (r == "true")
                    return true;
                await Task.Delay(150);
            }
            while (sw.ElapsedMilliseconds < timeoutMs);
            return false;
        }

        // ---- Targeting by visible text / label --------------------------------

        /// <summary>Finds form fields by their visible label. Returns a JSON array of candidates.</summary>
        public async Task<string> FindFieldByLabelAsync(string label, bool exact)
        {
            string raw = await _core.ExecuteScriptAsync(PageScripts.BuildFindFieldJs(label, exact));
            return PageScripts.DecodeJsString(raw);
        }

        /// <summary>
        /// Resolves a label to exactly one field selector. A unique best-ranked match wins
        /// outright (an explicit &lt;label for&gt; beats a loose name substring); anything
        /// genuinely ambiguous raises with the candidate list rather than guessing.
        /// </summary>
        public async Task<string> ResolveFieldSelectorAsync(string label, bool exact, int index)
        {
            string listJson = await FindFieldByLabelAsync(label, exact);
            JArray hits;
            try { hits = JArray.Parse(listJson); }
            catch { throw new InvalidOperationException("Field search failed: " + listJson); }

            if (hits.Count == 0)
                throw new ArgumentException($"No visible form field matched label '{label}'. Use browser_get_inputs to list the fields on this page.");

            if (index >= 0)
            {
                if (index >= hits.Count)
                    throw new ArgumentException($"index {index} is out of range; {hits.Count} field(s) matched '{label}'.");
                return hits[index]["selector"].Value<string>();
            }

            int bestRank = hits[0]["rank"].Value<int>();
            var best = hits.Where(h => h["rank"].Value<int>() == bestRank).ToList();

            if (best.Count > 1)
            {
                var summary = string.Join("\n", hits.Select(h =>
                    $"  index {h["index"]}: <{h["tag"]} type={h["type"]}> label \"{h["label"]}\" (matched by {h["matched_by"]})  selector: {h["selector"]}"));
                throw new ArgumentException(
                    $"{hits.Count} fields matched label '{label}' and {best.Count} are equally good. Re-call with 'index' (or exact=true):\n{summary}");
            }

            return best[0]["selector"].Value<string>();
        }

        /// <summary>Finds elements by visible text. Returns a JSON array of candidates.</summary>
        public async Task<string> FindByTextAsync(string text, bool exact, bool clickableOnly)
        {
            string js = PageScripts.BuildFindByTextJs(text, exact, clickableOnly, false, -1);
            string raw = await _core.ExecuteScriptAsync(js);
            return PageScripts.DecodeJsString(raw);
        }

        /// <summary>
        /// Clicks the element whose visible text matches. Ambiguity is an error rather
        /// than a guess: if several elements match and no index is given, the candidate
        /// list is returned so the caller can disambiguate in one follow-up call.
        /// </summary>
        public async Task<string> ClickByTextAsync(string text, bool exact, bool clickableOnly, int index, string button, int clickCount)
        {
            string listJson = await FindByTextAsync(text, exact, clickableOnly);
            JArray hits;
            try { hits = JArray.Parse(listJson); }
            catch { throw new InvalidOperationException("Element search failed: " + listJson); }

            if (hits.Count == 0)
                throw new ArgumentException($"No visible element matched text '{text}'" + (clickableOnly ? " (searching clickable elements only; set clickable_only=false to widen)." : "."));

            if (hits.Count > 1 && index < 0)
            {
                var summary = string.Join("\n", hits.Select(h =>
                    $"  index {h["index"]}: <{h["tag"]}> \"{h["text"]}\"  selector: {h["selector"]}"));
                throw new ArgumentException(
                    $"{hits.Count} elements matched text '{text}'. Re-call with 'index' to choose one (or use exact=true):\n{summary}");
            }

            int pick = index < 0 ? 0 : index;
            if (pick >= hits.Count)
                throw new ArgumentException($"index {pick} is out of range; {hits.Count} element(s) matched '{text}'.");

            // Scroll the chosen element into view, then re-read its coordinates.
            string scrollJs = PageScripts.BuildFindByTextJs(text, exact, clickableOnly, true, pick);
            string afterRaw = await _core.ExecuteScriptAsync(scrollJs);
            var after = JArray.Parse(PageScripts.DecodeJsString(afterRaw));
            if (pick >= after.Count)
                throw new InvalidOperationException("The matched element disappeared before it could be clicked.");

            var target = after[pick];
            double x = target["x"].Value<double>();
            double y = target["y"].Value<double>();

            await ClickAtAsync(x, y, button, clickCount);
            return $"Clicked <{target["tag"]}> \"{target["text"]}\" at ({x}, {y}). Selector: {target["selector"]}";
        }

        /// <summary>
        /// Dispatches a real (virtual) mouse click at viewport CSS-pixel
        /// coordinates via the DevTools Input domain — equivalent to a user
        /// clicking that spot, independent of any DOM selector.
        /// </summary>
        public async Task<string> ClickAtAsync(double x, double y, string button, int clickCount)
        {
            if (string.IsNullOrEmpty(button)) button = "left";
            int buttonsMask;
            switch (button)
            {
                case "right": buttonsMask = 2; break;
                case "middle": buttonsMask = 4; break;
                default: button = "left"; buttonsMask = 1; break;
            }
            if (clickCount < 1) clickCount = 1;

            // One target for the whole gesture, even if the press opens a new tab.
            CoreWebView2 core = _core;

            var moved = new JObject { ["type"] = "mouseMoved", ["x"] = x, ["y"] = y };
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", moved.ToString(Formatting.None));

            var pressed = new JObject
            {
                ["type"] = "mousePressed",
                ["x"] = x,
                ["y"] = y,
                ["button"] = button,
                ["buttons"] = buttonsMask,
                ["clickCount"] = clickCount
            };
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", pressed.ToString(Formatting.None));

            var released = new JObject
            {
                ["type"] = "mouseReleased",
                ["x"] = x,
                ["y"] = y,
                ["button"] = button,
                ["buttons"] = 0,
                ["clickCount"] = clickCount
            };
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", released.ToString(Formatting.None));

            return $"Dispatched {button} click x{clickCount} at viewport ({x}, {y}).";
        }
    }
}
