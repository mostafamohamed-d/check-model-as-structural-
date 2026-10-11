using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Status chips + Category/Severity/free-text filters. Raises FilterChanged with the
    /// current ResultFilter whenever a control changes; free-text is debounced with a short timer
    /// so fast typing doesn't refilter on every keystroke.</summary>
    public sealed class FilterToolbar : UserControl
    {
        private readonly StatusChips _chips;
        private readonly ComboBox _categoryCombo;
        private readonly ComboBox _severityCombo;
        private readonly TextBox _searchBox;
        private readonly Timer _debounce;
        private bool _suppress;

        public event EventHandler<ResultFilter> FilterChanged;

        public FilterToolbar()
        {
            Dock = DockStyle.Top;
            BackColor = Theme.Surface;
            Height = this.S(48);
            Padding = new Padding(this.S(16), this.S(10), this.S(16), this.S(8));

            _chips = new StatusChips { Dock = DockStyle.Fill };
            _chips.SelectedChanged += (s, e) => RaiseFilterChanged();

            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Padding = Padding.Empty };
            _categoryCombo = Combo(new object[] { "All categories" }, this.S(150));
            _severityCombo = Combo(new object[] { "Any severity" }.Concat(Enum.GetNames(typeof(Severity))).ToArray(), this.S(110));
            _searchBox = new TextBox { Width = this.S(220), Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(this.S(8), this.S(1), 0, 0) };
            Theme.SetCue(_searchBox, "Search results  (Ctrl+F)");
            right.Controls.Add(_categoryCombo);
            right.Controls.Add(_severityCombo);
            right.Controls.Add(_searchBox);

            Controls.Add(_chips);
            Controls.Add(right);

            _debounce = new Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); RaiseFilterChanged(); };

            _categoryCombo.SelectedIndexChanged += (s, e) => RaiseFilterChanged();
            _severityCombo.SelectedIndexChanged += (s, e) => RaiseFilterChanged();
            _searchBox.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
        }

        private ComboBox Combo(object[] items, int width)
        {
            var combo = Theme.StyleCombo(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(this.S(8), 0, 0, 0) });
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            return combo;
        }

        /// <summary>Populate the Category dropdown from the actual result set - keeps it in sync
        /// with whatever categories the current run actually produced.</summary>
        public void SetAvailableCategories(IEnumerable<string> categories)
        {
            var current = _categoryCombo.SelectedIndex > 0 ? (string)_categoryCombo.SelectedItem : null;
            _suppress = true;
            _categoryCombo.Items.Clear();
            _categoryCombo.Items.Add("All categories");
            foreach (var c in categories.Distinct().OrderBy(c => c)) _categoryCombo.Items.Add(c);
            _categoryCombo.SelectedItem = current != null && _categoryCombo.Items.Contains(current) ? current : (object)"All categories";
            _suppress = false;
        }

        public void SetCounts(ValidationSummary summary) => _chips.SetCounts(summary);

        /// <summary>Set status and category together (from a status-band click) with one refilter.</summary>
        public void Show(string category, ValidationStatus? status)
        {
            _suppress = true;
            _chips.Selected = status;
            _categoryCombo.SelectedItem = category != null && _categoryCombo.Items.Contains(category) ? category : (object)"All categories";
            _suppress = false;
            RaiseFilterChanged();
        }

        public void FocusSearch()
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        public ResultFilter CurrentFilter => new ResultFilter
        {
            Status = _chips.Selected,
            Category = _categoryCombo.SelectedIndex > 0 ? (string)_categoryCombo.SelectedItem : null,
            Severity = _severityCombo.SelectedIndex > 0 ? (Severity?)Enum.Parse(typeof(Severity), (string)_severityCombo.SelectedItem) : null,
            SearchText = _searchBox.Text
        };

        private void RaiseFilterChanged()
        {
            if (!_suppress) FilterChanged?.Invoke(this, CurrentFilter);
        }

        /// <summary>A single-select row of "All / Fail 12 / Warning 40 ..." chips.</summary>
        private sealed class StatusChips : Control
        {
            private readonly Dictionary<ValidationStatus, int> _counts = new Dictionary<ValidationStatus, int>();
            private int _total;
            private readonly List<KeyValuePair<Rectangle, ValidationStatus?>> _hits = new List<KeyValuePair<Rectangle, ValidationStatus?>>();
            private ValidationStatus? _selected;
            private Rectangle _hover;

            public event EventHandler SelectedChanged;

            public StatusChips()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                BackColor = Theme.Surface;
                Width = this.S(560);
            }

            public ValidationStatus? Selected
            {
                get => _selected;
                set { _selected = value; Invalidate(); }
            }

            public void SetCounts(ValidationSummary s)
            {
                _total = s.TotalCount;
                _counts[ValidationStatus.Pass] = s.PassCount;
                _counts[ValidationStatus.Warning] = s.WarningCount;
                _counts[ValidationStatus.Fail] = s.FailCount;
                _counts[ValidationStatus.Exempt] = s.ExemptCount;
                _counts[ValidationStatus.NotChecked] = s.NotCheckedCount;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                _hits.Clear();
                var x = 0;
                var h = this.S(26);
                var y = (Height - h) / 2;

                x = Chip(g, x, y, h, null, "All", _total, Theme.Accent);
                foreach (var s in Theme.StatusOrder)
                {
                    _counts.TryGetValue(s, out var n);
                    x = Chip(g, x, y, h, s, Theme.StatusText(s), n, Theme.ColorForStatus(s));
                }
            }

            private int Chip(Graphics g, int x, int y, int h, ValidationStatus? status, string label, int count, Color color)
            {
                var text = $"{label}  {count:N0}";
                var w = TextRenderer.MeasureText(g, text, Theme.BodyStrong, Size.Empty, TextFormatFlags.NoPadding).Width + this.S(status == null ? 20 : 30);
                var r = new Rectangle(x, y, w, h);
                var selected = _selected == status;
                var hover = _hover == r;
                using (var path = Theme.Rounded(r, h / 2))
                {
                    var fill = selected ? Theme.Tint(color, 0.82f) : hover ? Theme.SurfaceAlt : Theme.Surface;
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var p = new Pen(selected ? color : Theme.Line)) g.DrawPath(p, path);
                }
                var tx = r.X + this.S(10);
                if (status != null)
                {
                    var d = this.S(7);
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, tx, y + (h - d) / 2, d, d);
                    tx += this.S(12);
                }
                var fore = count == 0 && !selected ? Theme.InkMuted : selected ? Theme.Shade(color, 0.3f) : Theme.Ink;
                TextRenderer.DrawText(g, text, selected ? Theme.BodyStrong : Theme.Body, new Rectangle(tx, y, r.Right - tx, h), fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _hits.Add(new KeyValuePair<Rectangle, ValidationStatus?>(r, status));
                return r.Right + this.S(6);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var hit = _hits.FirstOrDefault(h => h.Key.Contains(e.Location)).Key;
                if (hit == _hover) return;
                _hover = hit;
                Cursor = hit.IsEmpty ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = Rectangle.Empty;
                Invalidate();
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                foreach (var h in _hits)
                {
                    if (!h.Key.Contains(e.Location)) continue;
                    Selected = h.Value;
                    SelectedChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
    }
}
