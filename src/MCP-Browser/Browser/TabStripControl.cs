using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCP_Browser.Browser
{
    internal sealed class TabStripItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    /// Lightweight owner-drawn browser tab strip:
    ///   &lt; [ Title  × ] [ Title  × ] [ Title  × ] &gt; [ + ]
    /// Supports click-to-switch, × / middle-click to close, + to open a new tab,
    /// horizontal scrolling (&lt; / &gt; and mouse wheel), and full title/URL tooltips.
    /// </summary>
    internal sealed class TabStripControl : Control
    {
        public const int StripHeight = 30;

        private const int ScrollBtnWidth = 24;
        private const int NewTabBtnWidth = 28;
        private const int MinTabWidth = 120;
        private const int MaxTabWidth = 210;
        private const int TabGap = 2;
        private const int CloseBtnSize = 16;
        private const int CloseBtnRightPad = 6;
        private const int TextLeftPad = 10;

        private readonly List<TabStripItem> _tabs = new List<TabStripItem>();
        private int _activeIndex = -1;
        private int _scrollOffset;

        private int _hoverTab = -1;
        private int _hoverCloseTab = -1;
        private bool _hoverLeft;
        private bool _hoverRight;
        private bool _hoverNew;

        private readonly ToolTip _toolTip = new ToolTip();
        private int _lastTooltipTab = -2;

        public event Action<int> TabSelected;
        public event Action<int> TabCloseRequested;
        public event Action NewTabRequested;

        public TabStripControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Height = StripHeight;
            Font = new Font("Segoe UI", 8.75F);
            BackColor = Color.FromArgb(226, 230, 235);
            TabStop = false;
        }

        public void SetTabs(IReadOnlyList<TabStripItem> tabs, int activeIndex)
        {
            _tabs.Clear();
            if (tabs != null) _tabs.AddRange(tabs);
            _activeIndex = activeIndex;
            EnsureActiveTabVisible();
            ClampScroll();
            Invalidate();
        }

        private Rectangle LeftButtonRect => new Rectangle(2, 3, ScrollBtnWidth, Height - 6);
        private Rectangle NewTabButtonRect => new Rectangle(Width - NewTabBtnWidth - 2, 3, NewTabBtnWidth, Height - 6);
        private Rectangle RightButtonRect => new Rectangle(NewTabButtonRect.Left - ScrollBtnWidth - 2, 3, ScrollBtnWidth, Height - 6);

        private Rectangle ViewportRect
        {
            get
            {
                int left = LeftButtonRect.Right + 3;
                int right = RightButtonRect.Left - 3;
                return new Rectangle(left, 0, Math.Max(0, right - left), Height);
            }
        }

        private int ComputeTabWidth()
        {
            if (_tabs.Count == 0) return MaxTabWidth;
            int avail = ViewportRect.Width;
            if (avail <= 0) return MinTabWidth;
            int totalGap = TabGap * Math.Max(0, _tabs.Count - 1);
            int ideal = (avail - totalGap) / _tabs.Count;
            return Math.Max(MinTabWidth, Math.Min(MaxTabWidth, ideal));
        }

        private int TotalTabsWidth()
        {
            if (_tabs.Count == 0) return 0;
            return _tabs.Count * ComputeTabWidth() + TabGap * (_tabs.Count - 1);
        }

        private int MaxScrollOffset()
        {
            return Math.Max(0, TotalTabsWidth() - ViewportRect.Width);
        }

        private void ClampScroll()
        {
            int max = MaxScrollOffset();
            if (_scrollOffset < 0) _scrollOffset = 0;
            if (_scrollOffset > max) _scrollOffset = max;
        }

        private void EnsureActiveTabVisible()
        {
            if (_activeIndex < 0 || _activeIndex >= _tabs.Count) return;
            Rectangle vp = ViewportRect;
            if (vp.Width <= 0) return;

            int tabW = ComputeTabWidth();
            int tabLeft = _activeIndex * (tabW + TabGap);
            int tabRight = tabLeft + tabW;

            if (tabLeft < _scrollOffset)
                _scrollOffset = tabLeft;
            else if (tabRight > _scrollOffset + vp.Width)
                _scrollOffset = tabRight - vp.Width;

            ClampScroll();
        }

        private Rectangle GetTabRect(int index)
        {
            Rectangle vp = ViewportRect;
            int tabW = ComputeTabWidth();
            int x = vp.Left + index * (tabW + TabGap) - _scrollOffset;
            return new Rectangle(x, 3, tabW, Height - 3);
        }

        private Rectangle GetCloseRect(Rectangle tabRect)
        {
            int x = tabRect.Right - CloseBtnRightPad - CloseBtnSize;
            int y = tabRect.Top + (tabRect.Height - CloseBtnSize) / 2;
            return new Rectangle(x, y, CloseBtnSize, CloseBtnSize);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background and subtle bottom divider
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);
            using (var borderPen = new Pen(Color.FromArgb(195, 200, 208)))
                g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

            bool canScrollLeft = _scrollOffset > 0;
            bool canScrollRight = _scrollOffset < MaxScrollOffset();

            DrawNavButton(g, LeftButtonRect, "‹", canScrollLeft, _hoverLeft);
            DrawNavButton(g, RightButtonRect, "›", canScrollRight, _hoverRight);
            DrawNavButton(g, NewTabButtonRect, "+", true, _hoverNew);

            // Clipped tab pills
            Rectangle vp = ViewportRect;
            if (vp.Width <= 0) return;

            Region oldClip = g.Clip;
            g.SetClip(vp);
            try
            {
                bool showClose = _tabs.Count > 1;
                for (int i = 0; i < _tabs.Count; i++)
                {
                    Rectangle r = GetTabRect(i);
                    if (r.Right < vp.Left || r.Left > vp.Right) continue;
                    DrawTab(g, i, _tabs[i], r, i == _activeIndex, i == _hoverTab, showClose, i == _hoverCloseTab);
                }
            }
            finally
            {
                g.Clip = oldClip;
            }
        }

        private void DrawNavButton(Graphics g, Rectangle rect, string glyph, bool enabled, bool hovered)
        {
            if (enabled && hovered)
            {
                using (var b = new SolidBrush(Color.FromArgb(206, 212, 220)))
                using (var path = RoundedRect(rect, 4))
                    g.FillPath(b, path);
            }

            Color fg = enabled ? Color.FromArgb(55, 65, 81) : Color.FromArgb(160, 166, 176);
            using (var f = new Font("Segoe UI", 10.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    g, glyph, f, rect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private void DrawTab(Graphics g, int index, TabStripItem item, Rectangle rect, bool active, bool hovered, bool showClose, bool closeHovered)
        {
            Color fill = active
                ? SystemColors.Control
                : (hovered ? Color.FromArgb(238, 241, 245) : Color.FromArgb(214, 219, 226));
            Color border = active
                ? Color.FromArgb(180, 186, 196)
                : Color.FromArgb(196, 202, 210);

            using (var path = TopRoundedRect(rect, 5))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(borderPen, path);
            }

            // Active tab blends seamlessly into the toolbar below
            if (active)
            {
                using (var blendPen = new Pen(SystemColors.Control))
                    g.DrawLine(blendPen, rect.Left + 1, Height - 1, rect.Right - 1, Height - 1);
            }

            Rectangle closeRect = GetCloseRect(rect);
            int textRight = showClose ? (closeRect.Left - 4) : (rect.Right - 8);
            Rectangle textRect = new Rectangle(rect.Left + TextLeftPad, rect.Top, Math.Max(0, textRight - (rect.Left + TextLeftPad)), rect.Height);

            string title = string.IsNullOrWhiteSpace(item.Title) ? "New Tab" : item.Title;
            Color textColor = active ? Color.FromArgb(24, 24, 27) : Color.FromArgb(75, 85, 99);
            using (var f = new Font(Font, active ? FontStyle.Regular : FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    g, title, f, textRect, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            if (showClose)
            {
                if (closeHovered)
                {
                    using (var cb = new SolidBrush(Color.FromArgb(220, 68, 68)))
                        g.FillEllipse(cb, closeRect);
                }

                Color xColor = closeHovered ? Color.White : Color.FromArgb(100, 110, 125);
                using (var pen = new Pen(xColor, 1.4F))
                {
                    int pad = 4;
                    g.DrawLine(pen, closeRect.Left + pad, closeRect.Top + pad, closeRect.Right - pad, closeRect.Bottom - pad);
                    g.DrawLine(pen, closeRect.Right - pad, closeRect.Top + pad, closeRect.Left + pad, closeRect.Bottom - pad);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath TopRoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        private void HitTest(Point pt, out int tabIndex, out bool onClose)
        {
            tabIndex = -1;
            onClose = false;
            Rectangle vp = ViewportRect;
            if (!vp.Contains(pt)) return;

            bool showClose = _tabs.Count > 1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                Rectangle r = GetTabRect(i);
                if (r.Contains(pt))
                {
                    tabIndex = i;
                    onClose = showClose && GetCloseRect(r).Contains(pt);
                    return;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool left = LeftButtonRect.Contains(e.Location);
            bool right = RightButtonRect.Contains(e.Location);
            bool add = NewTabButtonRect.Contains(e.Location);
            HitTest(e.Location, out int tab, out bool close);
            int closeTab = close ? tab : -1;

            if (left != _hoverLeft || right != _hoverRight || add != _hoverNew || tab != _hoverTab || closeTab != _hoverCloseTab)
            {
                _hoverLeft = left;
                _hoverRight = right;
                _hoverNew = add;
                _hoverTab = tab;
                _hoverCloseTab = closeTab;
                Invalidate();
            }

            if (tab != _lastTooltipTab)
            {
                _lastTooltipTab = tab;
                if (tab >= 0 && tab < _tabs.Count)
                {
                    TabStripItem item = _tabs[tab];
                    string tip = string.IsNullOrWhiteSpace(item.Url) || item.Url == "about:blank"
                        ? (item.Title ?? "New Tab")
                        : (item.Title + "\n" + item.Url);
                    _toolTip.SetToolTip(this, tip);
                }
                else if (add)
                {
                    _toolTip.SetToolTip(this, "New Tab (Ctrl+T)");
                }
                else if (left)
                {
                    _toolTip.SetToolTip(this, "Scroll tabs left");
                }
                else if (right)
                {
                    _toolTip.SetToolTip(this, "Scroll tabs right");
                }
                else
                {
                    _toolTip.SetToolTip(this, null);
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverLeft = _hoverRight = _hoverNew = false;
            _hoverTab = _hoverCloseTab = -1;
            _lastTooltipTab = -2;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                if (LeftButtonRect.Contains(e.Location))
                {
                    _scrollOffset -= 140;
                    ClampScroll();
                    Invalidate();
                    return;
                }
                if (RightButtonRect.Contains(e.Location))
                {
                    _scrollOffset += 140;
                    ClampScroll();
                    Invalidate();
                    return;
                }
                if (NewTabButtonRect.Contains(e.Location))
                {
                    NewTabRequested?.Invoke();
                    return;
                }

                HitTest(e.Location, out int tab, out bool onClose);
                if (tab >= 0)
                {
                    if (onClose)
                        TabCloseRequested?.Invoke(tab);
                    else
                        TabSelected?.Invoke(tab);
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                HitTest(e.Location, out int tab, out _);
                if (tab >= 0 && _tabs.Count > 1)
                    TabCloseRequested?.Invoke(tab);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScrollOffset() > 0)
            {
                _scrollOffset -= e.Delta / 2;
                ClampScroll();
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
