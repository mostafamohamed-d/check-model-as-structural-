using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Flat tab strip with an accent underline; shows one of its pages in a host panel.
    /// Replaces TabControl, whose chrome can't be restyled.</summary>
    internal sealed class TabStrip : Control
    {
        private readonly List<(string Title, Control Page)> _tabs = new List<(string, Control)>();
        private readonly List<Rectangle> _bounds = new List<Rectangle>();
        private int _selected;
        private int _hover = -1;

        public Panel Host { get; } = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            BackColor = Theme.Surface;
            Height = this.S(40);
        }

        public Func<int, string> Badge { get; set; }

        public void Add(string title, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = _tabs.Count == 0;
            _tabs.Add((title, page));
            Host.Controls.Add(page);
        }

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value < 0 || value >= _tabs.Count) return;
                _selected = value;
                for (var i = 0; i < _tabs.Count; i++) _tabs[i].Page.Visible = i == value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var line = new Pen(Theme.Line)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            _bounds.Clear();
            var measured = _tabs.Select((t, i) =>
            {
                var badge = Badge?.Invoke(i);
                var w = TextRenderer.MeasureText(g, t.Title, i == _selected ? Theme.BodyStrong : Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
                var bw = string.IsNullOrEmpty(badge) ? 0 : TextRenderer.MeasureText(g, badge, Theme.SmallStrong, Size.Empty, TextFormatFlags.NoPadding).Width + this.S(14);
                return (Badge: badge, W: w, BW: bw);
            }).ToList();
            // Side padding per tab: 12px normally, squeezed (down to 4px) when the tabs would not fit.
            var content = measured.Sum(m => m.W + m.BW);
            var pad = _tabs.Count == 0 ? this.S(12) : Math.Max(this.S(4), Math.Min(this.S(12), (Width - this.S(16) - content) / (2 * _tabs.Count)));
            var x = this.S(16);
            for (var i = 0; i < _tabs.Count; i++)
            {
                var font = i == _selected ? Theme.BodyStrong : Theme.Body;
                var text = _tabs[i].Title;
                var (badge, w, bw) = measured[i];
                var r = new Rectangle(x, 0, w + bw + 2 * pad, Height);
                _bounds.Add(r);
                var fore = i == _selected ? Theme.Ink : i == _hover ? Theme.Ink : Theme.InkMuted;
                TextRenderer.DrawText(g, text, font, new Rectangle(r.X + pad, 0, w + 2, Height), fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (bw > 0)
                {
                    var br = new Rectangle(r.X + pad + w + this.S(6), (Height - this.S(18)) / 2, bw - this.S(6), this.S(18));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var path = Theme.Rounded(br, br.Height / 2))
                    using (var b = new SolidBrush(Theme.Tint(Theme.Fail, 0.84f)))
                        g.FillPath(b, path);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; // keep the underline crisp
                    TextRenderer.DrawText(g, badge, Theme.SmallStrong, br, Theme.Fail, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (i == _selected)
                    using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X + pad - this.S(4), Height - this.S(3), r.Width - 2 * pad + this.S(8), this.S(3));
                x = r.Right;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = _bounds.FindIndex(b => b.Contains(e.Location));
            if (hit == _hover) return;
            _hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var hit = _bounds.FindIndex(b => b.Contains(e.Location));
            if (hit >= 0) SelectedIndex = hit;
        }
    }

    /// <summary>A thin line under the toolbar that runs while ETABS is being read or rules are
    /// running - the app's only ambient motion, and only while work is happening.</summary>
    internal sealed class ActivityLine : Control
    {
        private readonly Timer _timer = new Timer { Interval = 16 };
        private float _phase;

        public ActivityLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            Height = this.S(3);
            BackColor = Theme.Chrome;
            _timer.Tick += (s, e) => { _phase = (_phase + 0.012f) % 1.4f; Invalidate(); };
        }

        private bool _running;

        public bool Running
        {
            get => _running;
            set
            {
                _running = value;
                // Windows "animation effects" off -> a static bar instead of a moving one.
                _timer.Enabled = value && SystemInformation.UIEffectsEnabled;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (!_running) return;
            using (var b = new SolidBrush(Color.FromArgb(0x5B, 0xA3, 0xD6)))
            {
                if (!_timer.Enabled) { e.Graphics.FillRectangle(b, 0, 0, Width, Height); return; } // reduced motion
                var segW = Width * 0.3f;
                var x = (_phase - 0.3f) * Width;
                e.Graphics.FillRectangle(b, x, 0, segW, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Bottom bar: what the app is doing on the left, result counts on the right.
    /// Notify() shows a confirmation for a few seconds instead of a modal "OK" box.</summary>
    internal sealed class Footer : Control
    {
        private string _status = "Ready.";
        private string _right = "";
        private string _notice;
        private readonly Timer _noticeTimer = new Timer { Interval = 7000 };

        public Footer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Bottom;
            Height = this.S(28);
            BackColor = Theme.Base;
            _noticeTimer.Tick += (s, e) => { _noticeTimer.Stop(); _notice = null; Invalidate(); };
        }

        public string Status
        {
            get => _status;
            set { _status = value; Invalidate(); }
        }

        public string Right
        {
            get => _right;
            set { _right = value; Invalidate(); }
        }

        public void Notify(string text)
        {
            _notice = text;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var line = new Pen(Theme.Line)) g.DrawLine(line, 0, 0, Width, 0);
            var pad = this.S(16);
            var rightW = TextRenderer.MeasureText(g, _right, Theme.Small).Width;
            TextRenderer.DrawText(g, _right, Theme.Small, new Rectangle(Width - pad - rightW, 0, rightW, Height), Theme.InkMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            var left = new Rectangle(pad, 0, Width - 2 * pad - rightW - pad, Height);
            if (_notice != null)
            {
                var d = this.S(7);
                using (var b = new SolidBrush(Theme.Pass)) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.FillEllipse(b, left.X, (Height - d) / 2, d, d); }
                TextRenderer.DrawText(g, _notice, Theme.SmallStrong, new Rectangle(left.X + this.S(14), 0, left.Width - this.S(14), Height), Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, _status, Theme.Small, left, Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _noticeTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Connection indicator: a dot and the connected model's name, on the chrome.</summary>
    internal sealed class ConnectionBadge : Control
    {
        private bool _connected;
        private string _text = "Not connected";

        public ConnectionBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Chrome;
            Size = new Size(this.S(230), this.S(30));
            Margin = new Padding(this.S(6), 0, 0, 0);
        }

        public void Set(bool connected, string text)
        {
            _connected = connected;
            _text = text;
            FitWidth();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FitWidth();
        }

        private void FitWidth()
        {
            var textW = TextRenderer.MeasureText(_text, _connected ? Theme.BodyStrong : Theme.Body).Width;
            Width = Math.Min(this.S(260), this.S(20) + textW);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = this.S(8);
            var color = _connected ? Color.FromArgb(0x4C, 0xC0, 0x7E) : Theme.ChromeTextMuted;
            using (var b = new SolidBrush(color)) g.FillEllipse(b, 0, (Height - d) / 2, d, d);
            TextRenderer.DrawText(g, _text, _connected ? Theme.BodyStrong : Theme.Body, new Rectangle(d + this.S(8), 0, Width - d - this.S(8), Height),
                _connected ? Theme.ChromeText : Theme.ChromeTextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
