using MCP_Browser.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCP_Browser.Browser
{
    /// <summary>Raised when the user closes the browser window while a tool call is using it.</summary>
    public class BrowserWindowClosedException : Exception
    {
        public BrowserWindowClosedException()
            : base("The user closed the browser window while this action was running. " +
                   "Call the tool again — the window is reopened automatically.") { }
    }

    /// <summary>
    /// Owns the browser's UI thread and the lifetime of the browser window.
    ///
    /// Lifecycle
    ///  - Start() spins up a dedicated STA thread running a WinForms message loop
    ///    with NO window. The process therefore starts invisible.
    ///  - The first RunOnUi(...) call creates the window, shows it and brings it to
    ///    the front. All WebView2 work happens on that thread.
    ///  - The user may minimize or close the window whenever they like. Closing it
    ///    only disposes the window: the message loop, the MCP server and the
    ///    process keep running, and the next RunOnUi(...) call builds a new window
    ///    (reopening the pages that were open, see restore_tabs_on_reopen).
    ///
    /// This file holds the lifecycle and tab management; the page operations the
    /// tools call are in BrowserHost.Operations.cs, diagnostics capture in
    /// BrowserHost.Diagnostics.cs.
    /// </summary>
    public partial class BrowserHost
    {
        private const int InitTimeoutMs = 60000;
        private const int RestoreTimeoutMs = 15000;

        // ---- UI thread plumbing ----------------------------------------------
        private Thread _uiThread;
        private Control _invoker;                       // hidden; marshals calls onto the UI thread
        private readonly ManualResetEventSlim _uiReady = new ManualResetEventSlim(false);

        // ---- Window state (touched on the UI thread only) --------------------
        private BrowserForm _form;
        private CoreWebView2Environment _env;
        private Task _initTask;
        private Exception _initFailure;
        private TaskCompletionSource<bool> _closedTcs;
        private readonly List<BrowserTab> _tabs = new List<BrowserTab>();
        private int _activeIndex;
        private int _nextTabId = 1;

        // Pages to reopen when the window comes back after the user closed it.
        private List<string> _savedUrls = new List<string>();
        private int _savedActiveIndex;

        private bool HasWindow => _form != null && !_form.IsDisposed && _tabs.Count > 0;
        private BrowserTab ActiveTab => _tabs[_activeIndex];
        private WebView2 _webView => ActiveTab.View;
        private CoreWebView2 _core => ActiveTab.Core;

        // ---- Process-level lifecycle ----------------------------------------

        /// <summary>Starts the UI thread. No window is created.</summary>
        public void Start()
        {
            _uiThread = new Thread(UiThreadMain) { IsBackground = true, Name = "browser-ui" };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _uiReady.Wait();
        }

        private void UiThreadMain()
        {
            // A stray exception in a UI event handler must not pop a crash dialog
            // or take the MCP server down with it.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Diag.Log("UI thread exception: " + e.Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _invoker = new Control();
            _invoker.CreateControl();
            var forceHandle = _invoker.Handle;

            Diag.Log("UI thread ready (no window yet)");
            _uiReady.Set();

            // No main form: the loop outlives every browser window.
            Application.Run();
        }

        /// <summary>Closes the window (if any) and stops the UI thread.</summary>
        public void Shutdown()
        {
            try
            {
                _invoker.Invoke(new Action(() =>
                {
                    _savedUrls.Clear();
                    if (_form != null && !_form.IsDisposed) _form.Close();
                    Application.ExitThread();
                }));
            }
            catch { }
        }

        // ---- UI-thread marshaling -------------------------------------------

        /// <summary>
        /// Runs a browser operation on the UI thread and blocks until it finishes.
        /// Guarantees a live, initialised browser window first — creating one when
        /// none exists yet or the user has closed the previous one.
        /// </summary>
        public T RunOnUi<T>(Func<Task<T>> func)
        {
            var task = (Task<T>)_invoker.Invoke(new Func<Task<T>>(() => RunWithWindowAsync(func)));
            return task.GetAwaiter().GetResult();
        }

        public void RunOnUi(Func<Task> func)
        {
            RunOnUi(async () => { await func(); return true; });
        }

        /// <summary>Runs on the UI thread WITHOUT opening a window.</summary>
        private T InvokeUi<T>(Func<T> func)
        {
            return (T)_invoker.Invoke(func);
        }

        private async Task<T> RunWithWindowAsync<T>(Func<Task<T>> func)
        {
            Task closed = await EnsureWindowAsync();

            Task<T> work;
            try
            {
                work = func();
            }
            catch (Exception) when (closed.IsCompleted)
            {
                throw new BrowserWindowClosedException();
            }

            // WebView2 calls that are in flight when the window is destroyed may
            // never complete; racing them against the close keeps the server alive.
            if (await Task.WhenAny(work, closed) != work)
            {
                var ignored = work.ContinueWith(t => { var e = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new BrowserWindowClosedException();
            }

            try
            {
                return await work;
            }
            catch (Exception) when (closed.IsCompleted)
            {
                throw new BrowserWindowClosedException();
            }
        }

        // ---- Window lifecycle ------------------------------------------------

        /// <summary>
        /// Makes sure a browser window exists and its WebView2 is ready. Returns a
        /// task that completes when THAT window is closed.
        /// </summary>
        private async Task<Task> EnsureWindowAsync()
        {
            bool created = false;
            if (_form == null || _form.IsDisposed)
            {
                CreateWindow();
                created = true;
            }

            BrowserForm form = _form;
            Task closed = _closedTcs.Task;
            Task init = _initTask;

            Task first = await Task.WhenAny(init, closed, Task.Delay(InitTimeoutMs));

            // A failed start-up closes its own window; report the real cause
            // rather than blaming the user for the close.
            if (_initFailure != null && init.IsCompleted)
            {
                Exception failure = _initFailure;
                throw new InvalidOperationException("The browser could not start: " + failure.Message, failure);
            }
            if (first == closed)
                throw new BrowserWindowClosedException();
            if (first != init)
                throw new TimeoutException("WebView2 did not initialize within 60 s. Is the Microsoft Edge WebView2 Runtime installed?");
            await init;     // surfaces an initialisation failure

            // A freshly opened window was already raised in CreateWindow().
            if (!created && McpConfig.BringToFrontOnEveryCall)
                NativeMethods.ForceForeground(form);

            return closed;
        }

        private void CreateWindow()
        {
            Diag.Log("CreateWindow");
            _tabs.Clear();
            _activeIndex = 0;
            _closedTcs = new TaskCompletionSource<bool>();
            _initFailure = null;

            var form = new BrowserForm();
            form.NavigateRequested += url => { if (HasWindow) TryUi(() => _core.Navigate(url)); };
            form.BackRequested += () => { if (HasWindow && _core.CanGoBack) TryUi(() => _core.GoBack()); };
            form.ForwardRequested += () => { if (HasWindow && _core.CanGoForward) TryUi(() => _core.GoForward()); };
            form.ReloadRequested += () => { if (HasWindow) TryUi(() => _core.Reload()); };
            form.TabSelected += index => { if (HasWindow && index >= 0 && index < _tabs.Count) SetActiveTab(index); };
            form.TabCloseRequested += index =>
            {
                if (!HasWindow || _tabs.Count <= 1) return;
                int target = index < 0 ? _activeIndex : index;
                if (target >= 0 && target < _tabs.Count) RemoveTab(target);
            };
            form.NewTabRequested += async () =>
            {
                if (!HasWindow) return;
                try
                {
                    BrowserTab t = await AddTabAsync();
                    SetActiveTab(_tabs.IndexOf(t));
                }
                catch (Exception ex) { Diag.Log("NewTabRequested failed: " + ex.Message); }
            };
            form.FormClosed += (s, e) => OnWindowClosed(form);

            _form = form;
            form.Show();

            // The AI just asked for the browser: put it in front of the user.
            NativeMethods.ForceForeground(form);

            _initTask = InitWindowAsync(form);
        }

        private async Task InitWindowAsync(BrowserForm form)
        {
            try
            {
                Diag.Log("InitWindowAsync: userDataFolder=" + McpConfig.UserDataFolder);
                _env = await CoreWebView2Environment.CreateAsync(null, McpConfig.UserDataFolder, null);

                await AddTabAsync();
                SetActiveTab(0);

                await RestoreSavedTabsAsync();
                Diag.Log("InitWindowAsync: ready");
            }
            catch (Exception ex)
            {
                Diag.Log("InitWindowAsync: EXCEPTION " + ex);
                // Leave nothing half-built behind: the next call starts clean.
                // (When the user closed the window mid-start, _form is already gone.)
                if (_form == form && !form.IsDisposed)
                {
                    _initFailure = ex;
                    _savedUrls.Clear();
                    form.Close();
                }
                throw;
            }
        }

        /// <summary>Reopens the pages that were showing when the user closed the window.</summary>
        private async Task RestoreSavedTabsAsync()
        {
            List<string> urls = _savedUrls;
            int active = _savedActiveIndex;
            _savedUrls = new List<string>();

            if (!McpConfig.RestoreTabsOnReopen || !urls.Any(UrlUtil.IsRestorable))
                return;

            for (int i = 0; i < urls.Count; i++)
            {
                BrowserTab tab = i == 0 ? _tabs[0] : await AddTabAsync();
                if (!UrlUtil.IsRestorable(urls[i])) continue;
                try
                {
                    string url = urls[i];
                    await RunNavigationAsync(tab.Core, () => tab.Core.Navigate(url), "restore", RestoreTimeoutMs);
                }
                catch (Exception ex)
                {
                    Diag.Log("Restore of " + urls[i] + " failed: " + ex.Message);
                }
            }

            SetActiveTab(Math.Max(0, Math.Min(active, _tabs.Count - 1)));
        }

        private void OnWindowClosed(BrowserForm form)
        {
            if (_form != form) return;
            Diag.Log("Window closed by user; process stays alive");

            _savedUrls = _tabs.Select(t => t.LastUrl).ToList();
            _savedActiveIndex = _activeIndex;

            _tabs.Clear();
            _activeIndex = 0;
            _form = null;
            _env = null;
            _closedTcs.TrySetResult(true);
        }

        private static void TryUi(Action act)
        {
            try { act(); }
            catch (Exception ex) { Diag.Log("UI action failed: " + ex.Message); }
        }

        // ---- Window visibility (the show/hide tools) --------------------------

        /// <summary>Restores, raises and focuses the window. Call inside RunOnUi.</summary>
        public Task<string> ShowWindowAsync()
        {
            NativeMethods.ForceForeground(_form);
            if (!NativeMethods.IsShown(_form))
                return Task.FromResult("Tried to show the browser window, but Windows still reports it as hidden or minimized. " +
                                       "The page keeps working; ask the user to restart the AI client if they need to see it.");
            return Task.FromResult("Browser window is open, in front and focused.");
        }

        /// <summary>Minimizes the window to the taskbar. Never opens one.</summary>
        public string MinimizeWindow()
        {
            return InvokeUi(() =>
            {
                if (_form == null || _form.IsDisposed)
                    return "Browser window is not open; nothing to hide.";
                _form.WindowState = FormWindowState.Minimized;
                return "Browser window minimized to the taskbar. It keeps working; a screenshot restores it.";
            });
        }

        // ---- Tabs ------------------------------------------------------------

        private async Task<BrowserTab> AddTabAsync()
        {
            BrowserForm form = _form;
            var tab = new BrowserTab
            {
                Id = _nextTabId++,
                View = new WebView2 { Dock = DockStyle.Fill }
            };

            // The control must be visible while its handle is created, otherwise
            // EnsureCoreWebView2Async has no window to attach to. SetActiveTab
            // fixes up visibility once initialisation has completed.
            form.Content.Controls.Add(tab.View);

            await tab.View.EnsureCoreWebView2Async(_env);
            tab.Core = tab.View.CoreWebView2;
            tab.LastUrl = tab.Core.Source ?? "";

            var settings = tab.Core.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsStatusBarEnabled = false;

            tab.Core.SourceChanged += (s, e) =>
            {
                tab.LastUrl = tab.Core.Source ?? "";
                UpdateToolbar();
                UpdateTabBar();
            };
            tab.Core.DocumentTitleChanged += (s, e) => UpdateTabBar();
            tab.Core.HistoryChanged += (s, e) => UpdateToolbar();

            // A new top-level navigation starts a fresh diagnostic window, so
            // captured console/network entries always describe the current page.
            tab.Core.NavigationStarting += (s, e) =>
            {
                tab.ClearLogs();
                tab.ForgetRequests();
            };

            // window.open / target=_blank would otherwise spawn a detached popup
            // window that no tool can see or drive. Host it as a tab instead.
            tab.Core.NewWindowRequested += OnNewWindowRequested;
            tab.Core.WindowCloseRequested += (s, e) => OnTabAskedToClose(tab);

            // Downloads are saved silently into McpConfig.DownloadFolder and reported
            // back to the agent: a Save dialog would wait for a human who may not be there.
            DownloadTracker.Attach(tab.Core);

            if (McpConfig.AutoAcceptDialogs)
            {
                settings.AreDefaultScriptDialogsEnabled = false;
                tab.Core.ScriptDialogOpening += (s, e) => OnScriptDialog(tab, e);
            }

            await EnableDiagnosticsAsync(tab);

            _tabs.Add(tab);
            UpdateTabBar();
            return tab;
        }

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            CoreWebView2Deferral deferral = e.GetDeferral();
            try
            {
                BrowserTab tab = await AddTabAsync();
                e.NewWindow = tab.Core;     // keeps window.opener and the pending request intact
                e.Handled = true;
                SetActiveTab(_tabs.IndexOf(tab));
                Diag.Log("Popup hosted as tab " + tab.Id);
            }
            catch (Exception ex)
            {
                Diag.Log("NewWindowRequested failed: " + ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnTabAskedToClose(BrowserTab tab)
        {
            int index = _tabs.IndexOf(tab);
            if (index < 0 || _tabs.Count <= 1) return;
            RemoveTab(index);
        }

        private void OnScriptDialog(BrowserTab tab, CoreWebView2ScriptDialogOpeningEventArgs e)
        {
            string kind = e.Kind.ToString().ToLowerInvariant();
            tab.Add(new LogEntry
            {
                Kind = "console",
                Level = "info",
                Text = $"[dialog:{kind}] {e.Message} (auto-accepted)",
                Url = e.Uri,
                TimeUtc = DateTime.UtcNow
            });

            if (e.Kind == CoreWebView2ScriptDialogKind.Prompt)
                e.ResultText = e.DefaultText;
            e.Accept();
        }

        private void SetActiveTab(int index)
        {
            _activeIndex = index;
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].View.Visible = (i == index);

            _tabs[index].View.BringToFront();
            UpdateToolbar();
            UpdateTabBar();
        }

        private void UpdateTabBar()
        {
            if (_form == null || _form.IsDisposed) return;
            var items = new List<TabStripItem>(_tabs.Count);
            foreach (BrowserTab t in _tabs)
            {
                string url = t.LastUrl ?? "";
                string title = "";
                try { title = t.Core?.DocumentTitle ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(title))
                {
                    if (!string.IsNullOrWhiteSpace(url) && url != "about:blank")
                    {
                        try { title = new Uri(url).Host; } catch { }
                        if (string.IsNullOrWhiteSpace(title)) title = url;
                    }
                    else
                    {
                        title = "New Tab";
                    }
                }
                items.Add(new TabStripItem { Id = t.Id, Title = title, Url = url });
            }
            _form.UpdateTabs(items, _activeIndex);
        }

        private void UpdateToolbar()
        {
            if (!HasWindow) return;
            CoreWebView2 core = ActiveTab.Core;
            if (core == null) return;
            try { _form.SetNavigationState(core.Source, core.CanGoBack, core.CanGoForward); }
            catch { }
        }

        private void RemoveTab(int index)
        {
            BrowserTab tab = _tabs[index];
            _tabs.RemoveAt(index);

            try
            {
                _form.Content.Controls.Remove(tab.View);
                tab.View.Dispose();
            }
            catch (Exception ex)
            {
                Diag.Log("RemoveTab dispose failed: " + ex.Message);
            }

            if (_activeIndex >= _tabs.Count) _activeIndex = _tabs.Count - 1;
            else if (_activeIndex > index) _activeIndex--;
            SetActiveTab(_activeIndex);
        }
    }
}
