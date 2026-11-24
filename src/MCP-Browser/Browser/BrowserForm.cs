using MCP_Browser.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MCP_Browser.Browser
{
    /// <summary>
    /// The browser window: a toolbar (back / forward / reload / address / Go /
    /// About) above a content area that hosts one WebView2 control per tab.
    /// It is an ordinary top-level window — shown on the taskbar, and free to be
    /// moved, resized, minimized or closed by the user. It holds no browser logic;
    /// BrowserHost owns that and listens to the events below.
    /// </summary>
    internal class BrowserForm : Form
    {
        private const int ToolbarHeight = 36;

        private readonly TabStripControl _tabBar;
        private readonly TableLayoutPanel _toolbar;
        private readonly TextBox _address;
        private readonly Button _back;
        private readonly Button _forward;
        private readonly Panel _content;

        public event Action<string> NavigateRequested;
        public event Action BackRequested;
        public event Action ForwardRequested;
        public event Action ReloadRequested;
        public event Action<int> TabSelected;
        public event Action<int> TabCloseRequested;
        public event Action NewTabRequested;

        /// <summary>Parent for the tabs' WebView2 controls; its client size is the page viewport.</summary>
        public Panel Content => _content;

        public BrowserForm()
        {
            Text = AppInfo.WindowTitle;
            Icon = AppInfo.Icon;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(420, 260);
            KeyPreview = true;

            _toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = ToolbarHeight,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(4, 4, 4, 4),
                BackColor = SystemColors.Control
            };
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var tips = new ToolTip();

            _back = MakeButton("←", "Back", tips);
            _back.Click += (s, e) => BackRequested?.Invoke();

            _forward = MakeButton("→", "Forward", tips);
            _forward.Click += (s, e) => ForwardRequested?.Invoke();

            var reload = MakeButton("↻", "Reload (F5)", tips);
            reload.Click += (s, e) => ReloadRequested?.Invoke();

            _address = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(4, 2, 4, 0)
            };
            _address.KeyDown += OnAddressKeyDown;

            var go = MakeButton("Go", "Open the address", tips);
            go.Click += (s, e) => SubmitAddress();

            var about = MakeButton("About", "About " + AppInfo.ProductName, tips);
            about.Click += (s, e) => ShowAbout();

            _toolbar.Controls.Add(_back, 0, 0);
            _toolbar.Controls.Add(_forward, 1, 0);
            _toolbar.Controls.Add(reload, 2, 0);
            _toolbar.Controls.Add(_address, 3, 0);
            _toolbar.Controls.Add(go, 4, 0);
            _toolbar.Controls.Add(about, 5, 0);

            _tabBar = new TabStripControl { Dock = DockStyle.Top };
            _tabBar.TabSelected += i => TabSelected?.Invoke(i);
            _tabBar.TabCloseRequested += i => TabCloseRequested?.Invoke(i);
            _tabBar.NewTabRequested += () => NewTabRequested?.Invoke();

            _content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            // Docking is resolved back-to-front: add the Fill panel first, then the
            // toolbar, then the tab strip so the tab strip claims the very top edge.
            Controls.Add(_content);
            Controls.Add(_toolbar);
            Controls.Add(_tabBar);

            SetViewportSize(McpConfig.WindowWidth, McpConfig.WindowHeight);
            CenterOnWorkingArea();
        }

        private static Button MakeButton(string text, string tip, ToolTip tips)
        {
            var b = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(1, 0, 1, 0),
                FlatStyle = FlatStyle.System,
                Font = new Font("Segoe UI", 9F),
                TabStop = false
            };
            tips.SetToolTip(b, tip);
            return b;
        }

        /// <summary>
        /// Sizes the window so the PAGE area (not the whole client area) is exactly
        /// width x height CSS pixels — the toolbar is added on top.
        /// </summary>
        public void SetViewportSize(int width, int height)
        {
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            ClientSize = new Size(width, height + _toolbar.Height + _tabBar.Height);
        }

        /// <summary>Updates the visible tab strip to match BrowserHost's tab list.</summary>
        public void UpdateTabs(IReadOnlyList<TabStripItem> tabs, int activeIndex)
        {
            _tabBar.SetTabs(tabs, activeIndex);
        }

        private void CenterOnWorkingArea()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Width, wa.Width);
            int h = Math.Min(Height, wa.Height);
            if (w != Width || h != Height) Size = new Size(w, h);
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
        }

        /// <summary>Reflects the active tab in the toolbar.</summary>
        public void SetNavigationState(string url, bool canGoBack, bool canGoForward)
        {
            // Do not yank the text out from under someone who is typing an address.
            // (Modified is set by keyboard edits and cleared by assigning Text.)
            if (!(_address.Focused && _address.Modified))
                _address.Text = url == "about:blank" ? "" : (url ?? "");
            _back.Enabled = canGoBack;
            _forward.Enabled = canGoForward;
        }

        private void OnAddressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SubmitAddress();
            }
        }

        private void SubmitAddress()
        {
            string url = UrlUtil.Normalize(_address.Text);
            if (url.Length == 0) return;
            _address.Modified = false;  // the address box tracks the page again
            NavigateRequested?.Invoke(url);
        }

        private void ShowAbout()
        {
            using (var dlg = new AboutDialog())
                dlg.ShowDialog(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                ReloadRequested?.Invoke();
            }
            else if ((e.Control && e.KeyCode == Keys.L) || (e.Alt && e.KeyCode == Keys.D))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _address.Focus();
                _address.SelectAll();
            }
            else if (e.Control && e.KeyCode == Keys.T)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                NewTabRequested?.Invoke();
            }
            else if (e.Control && e.KeyCode == Keys.W)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                TabCloseRequested?.Invoke(-1);
            }
        }
    }
}
