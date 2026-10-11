using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>
    /// The model verdict plus one stacked bar per category, read like an ETABS utilization
    /// legend: worst segments on the left. Clicking a segment (or a category name) asks the host
    /// to filter the results to it. Painted by hand - a view over ValidationSummary, no
    /// aggregation of its own.
    /// </summary>
    public sealed class StatusBand : Control
    {
        private sealed class Hit
        {
            public Rectangle Bounds;
            public string Category;
            public ValidationStatus? Status;
            public string Tip;
        }

        private ValidationSummary _summary;
        private readonly List<Hit> _hits = new List<Hit>();
        private Hit _hover;
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>(category, status) - either may be null meaning "any".</summary>
        public event Action<string, ValidationStatus?> SegmentClicked;

        public StatusBand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            BackColor = Theme.Surface;
            Height = this.S(96);
        }

        public void SetSummary(ValidationSummary summary)
        {
            _summary = summary;
            UpdateHeight();
            Invalidate();
        }

        private int Columns => Math.Max(1, (Width - LeftWidth - this.S(24)) / this.S(330));
        private int LeftWidth => this.S(300);
        private int RowHeight => this.S(26);

        private void UpdateHeight()
        {
            var n = _summary?.ByCategory.Count ?? 0;
            var rows = n == 0 ? 0 : (n + Columns - 1) / Columns;
            Height = Math.Max(this.S(96), this.S(28) + rows * RowHeight);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_summary != null) UpdateHeight(); // column count depends on width
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            _hits.Clear();
            using (var line = new Pen(Theme.Line)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);

            if (_summary == null)
            {
                PaintEmpty(g);
                return;
            }

            PaintVerdict(g);
            PaintCategories(g);
        }

        private void PaintEmpty(Graphics g)
        {
            var r = new Rectangle(this.S(24), 0, Width - this.S(48), Height);
            using (var f = Theme.DisplayStrong(15f))
                TextRenderer.DrawText(g, "No results yet", f, new Rectangle(r.X, this.S(20), r.Width, this.S(28)), Theme.Ink, TextFormatFlags.Left);
            TextRenderer.DrawText(g, "Connect to a running ETABS model, choose a profile and press Run (F5). Each category's checks will show here as a bar, worst first.",
                Theme.Body, new Rectangle(r.X, this.S(52), r.Width, this.S(40)), Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }

        private void PaintVerdict(Graphics g)
        {
            var s = _summary;
            var a = s.Assessment;
            var verdict = a.Verdict;
            // The run verdict, not the worst result: NOT_CHECKED / missing tables / exemptions can never read as "passes".
            var color = Theme.ColorForStatus(
                verdict == RunVerdict.Fail ? ValidationStatus.Fail :
                verdict == RunVerdict.Incomplete ? ValidationStatus.NotChecked :
                verdict == RunVerdict.ReviewRequired || verdict == RunVerdict.PassWithWarnings ? ValidationStatus.Warning :
                ValidationStatus.Pass);

            // Status rule down the left edge: the one strong mark on the page.
            using (var b = new SolidBrush(color)) g.FillRectangle(b, 0, 0, this.S(6), Height - 1);

            var x = this.S(24);
            var headline =
                verdict == RunVerdict.Fail ? "Model fails" :
                verdict == RunVerdict.Incomplete ? "Incomplete - not verified" :
                verdict == RunVerdict.ReviewRequired ? "Review required" :
                verdict == RunVerdict.PassWithWarnings ? "Model passes with warnings" :
                "Model passes";
            using (var f = Theme.DisplayStrong(verdict == RunVerdict.Pass || verdict == RunVerdict.Fail ? 20f : 15f))
                TextRenderer.DrawText(g, headline, f, new Rectangle(x, this.S(14), LeftWidth - x, this.S(34)), Theme.Shade(color, 0.1f), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var parts = new List<string>();
            if (s.FailCount > 0) parts.Add($"{s.FailCount:N0} {(s.FailCount == 1 ? "failure" : "failures")}");
            if (s.WarningCount > 0) parts.Add($"{s.WarningCount:N0} {(s.WarningCount == 1 ? "warning" : "warnings")}");
            if (s.NotCheckedCount > 0) parts.Add($"{s.NotCheckedCount:N0} not checked");
            if (s.ExemptCount > 0) parts.Add($"{s.ExemptCount:N0} exempt");
            var lead = parts.Count == 0 ? "Every check passed" : JoinWords(parts);
            var scope = a.CategoriesTotal > 0 && a.CategoriesRun < a.CategoriesTotal ? $" Only {a.CategoriesRun} of {a.CategoriesTotal} categories ran." : "";
            var firstBlocker = verdict == RunVerdict.Incomplete ? a.IncompleteReasons.FirstOrDefault()
                             : verdict == RunVerdict.ReviewRequired ? a.ReviewReasons.FirstOrDefault() : null;
            var sentence = $"{lead} across {s.TotalCount:N0} checked items.{scope}" + (firstBlocker != null ? " " + firstBlocker : "");
            TextRenderer.DrawText(g, sentence, Theme.Body, new Rectangle(x, this.S(50), LeftWidth - x - this.S(12), this.S(40)), Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak);

            using (var line = new Pen(Theme.Line)) g.DrawLine(line, LeftWidth, this.S(14), LeftWidth, Height - this.S(14));
        }

        private static string JoinWords(List<string> parts) =>
            parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts.Last();

        private void PaintCategories(Graphics g)
        {
            var cats = _summary.ByCategory.Values
                .OrderByDescending(c => c.FailCount).ThenByDescending(c => c.WarningCount).ThenBy(c => c.Category)
                .ToList();
            var cols = Columns;
            var left = LeftWidth + this.S(20);
            var colWidth = (Width - left - this.S(16)) / cols;
            var top = this.S(14);
            var nameW = this.S(118);
            var countW = this.S(70);

            for (var i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                var cell = new Rectangle(left + (i / RowsFor(cats.Count, cols)) * colWidth, top + (i % RowsFor(cats.Count, cols)) * RowHeight, colWidth - this.S(16), RowHeight);

                var nameRect = new Rectangle(cell.X, cell.Y, nameW, cell.Height);
                var hoverName = _hover != null && _hover.Category == c.Category && _hover.Status == null;
                TextRenderer.DrawText(g, c.Category, hoverName ? Theme.BodyStrong : Theme.Body, nameRect, Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                _hits.Add(new Hit { Bounds = nameRect, Category = c.Category, Tip = $"Show all {c.Category} results" });

                var barRect = new Rectangle(cell.X + nameW, cell.Y + (cell.Height - this.S(10)) / 2, Math.Max(this.S(40), cell.Width - nameW - countW), this.S(10));
                PaintBar(g, c, barRect);

                var problem = c.FailCount > 0 ? $"{c.FailCount:N0} fail" : c.WarningCount > 0 ? $"{c.WarningCount:N0} warn" : c.NotCheckedCount > 0 ? $"{c.NotCheckedCount:N0} n/c" : "OK";
                var problemColor = c.FailCount > 0 ? Theme.Fail : c.WarningCount > 0 ? Theme.Shade(Theme.Warning) : c.NotCheckedCount > 0 ? Theme.NotChecked : Theme.Pass;
                TextRenderer.DrawText(g, problem, Theme.SmallStrong, new Rectangle(barRect.Right + this.S(8), cell.Y, countW - this.S(8), cell.Height), problemColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        private static int RowsFor(int count, int cols) => Math.Max(1, (count + cols - 1) / cols);

        private void PaintBar(Graphics g, CategorySummary c, Rectangle bar)
        {
            using (var bg = new SolidBrush(Theme.Base)) g.FillRectangle(bg, bar);
            var counts = Theme.StatusOrder.Select(s => new { Status = s, Count = CountOf(c, s) }).Where(x => x.Count > 0).ToList();
            var total = counts.Sum(x => x.Count);
            if (total == 0) return;

            var minW = this.S(3);
            var x = bar.X;
            for (var i = 0; i < counts.Count; i++)
            {
                var w = i == counts.Count - 1 ? bar.Right - x : Math.Max(minW, (int)Math.Round(bar.Width * (double)counts[i].Count / total));
                w = Math.Min(w, bar.Right - x);
                if (w <= 0) break;
                var seg = new Rectangle(x, bar.Y, w, bar.Height);
                var color = Theme.ColorForStatus(counts[i].Status);
                var isHover = _hover != null && _hover.Category == c.Category && _hover.Status == counts[i].Status;
                using (var b = new SolidBrush(isHover ? Theme.Shade(color, 0.25f) : color)) g.FillRectangle(b, seg);
                if (x > bar.X) using (var sep = new Pen(Theme.Surface)) g.DrawLine(sep, x, bar.Y, x, bar.Bottom - 1);
                var hitRect = Rectangle.Inflate(seg, 0, this.S(6));
                _hits.Add(new Hit { Bounds = hitRect, Category = c.Category, Status = counts[i].Status, Tip = $"{c.Category}: {counts[i].Count:N0} {Theme.StatusText(counts[i].Status).ToLowerInvariant()}. Click to show them." });
                x += w;
            }
        }

        private static int CountOf(CategorySummary c, ValidationStatus s)
        {
            switch (s)
            {
                case ValidationStatus.Pass: return c.PassCount;
                case ValidationStatus.Warning: return c.WarningCount;
                case ValidationStatus.Fail: return c.FailCount;
                case ValidationStatus.Exempt: return c.ExemptCount;
                default: return c.NotCheckedCount;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = _hits.LastOrDefault(h => h.Bounds.Contains(e.Location));
            if (hit == _hover) return;
            _hover = hit;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            if (hit != null) _tip.Show(hit.Tip, this, e.X + this.S(12), e.Y + this.S(18), 4000); else _tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            _tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var hit = _hits.LastOrDefault(h => h.Bounds.Contains(e.Location));
            if (hit != null) SegmentClicked?.Invoke(hit.Category, hit.Status);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
