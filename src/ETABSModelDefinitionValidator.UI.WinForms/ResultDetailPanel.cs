using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Detail view of one selected result: a header with the finding (object, field,
    /// expected vs actual, source row), followed by the plain-language explanation of the check
    /// that produced it.</summary>
    public sealed class ResultDetailPanel : UserControl
    {
        private readonly FindingHeader _header;
        private readonly RichTextBox _box;
        private ValidationProfileConfig _config = new ValidationProfileConfig();

        public ResultDetailPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Surface;

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(this.S(24), this.S(8), this.S(24), this.S(8)), BackColor = Theme.Surface };
            _box = ExplanationWriter.NewBox();
            body.Controls.Add(_box);

            _header = new FindingHeader();
            Controls.Add(body);
            Controls.Add(_header);
        }

        public void SetConfig(ValidationProfileConfig config) => _config = config ?? new ValidationProfileConfig();

        public void SetResult(ValidationResult r)
        {
            _header.Result = r;
            _box.Clear();
            if (r == null) return;

            ExplanationWriter.Section(_box, "What was found", r.Message);
            ExplanationWriter.Section(_box, "Technical explanation", r.TechnicalExplanation);
            if (r.CodeBasis != null) ExplanationWriter.Section(_box, "Code reference", r.CodeBasis.ToString());

            var e = RuleCatalog.Find(r.RuleId, _config);
            if (e != null) ExplanationWriter.Explanation(_box, e);

            ExplanationWriter.Section(_box, "Where to find it",
                $"Source {r.Source?.WorkbookName}, table {r.Source?.TableName}, row {r.Source?.RowNumber}. Severity {r.Severity}.");

            _box.SelectionStart = 0;
            _box.ScrollToCaret();
        }

        /// <summary>Painted summary of one finding: status, rule, and a row of labelled values
        /// with Actual coloured by status so the mismatch is the first thing read.</summary>
        private sealed class FindingHeader : Control
        {
            private ValidationResult _result;

            public FindingHeader()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Dock = DockStyle.Top;
                BackColor = Theme.Surface;
                Height = this.S(112);
            }

            public ValidationResult Result
            {
                get => _result;
                set { _result = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                using (var line = new Pen(Theme.Line)) g.DrawLine(line, this.S(24), Height - 1, Width - this.S(24), Height - 1);
                var x = this.S(24);
                var r = _result;

                if (r == null)
                {
                    TextRenderer.DrawText(g, "Select a result to see what was checked, what ETABS has, and how to fix it.", Theme.Body,
                        new Rectangle(x, 0, Width - 2 * x, Height), Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    return;
                }

                Theme.DrawStatusPill(g, new Rectangle(x - this.S(8), this.S(12), this.S(140), this.S(28)), r.Status, this);
                var pillW = TextRenderer.MeasureText(Theme.StatusText(r.Status), Theme.SmallStrong, Size.Empty, TextFormatFlags.NoPadding).Width + this.S(28);
                var tx = x + pillW + this.S(8);
                using (var f = Theme.DisplayStrong(13f))
                {
                    var idW = TextRenderer.MeasureText(g, r.RuleId, f, Size.Empty, TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, r.RuleId, f, new Rectangle(tx, this.S(12), idW + 4, this.S(28)), Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    tx += idW + this.S(10);
                }
                TextRenderer.DrawText(g, r.RuleName, Theme.Body, new Rectangle(tx, this.S(12), Width - tx - x, this.S(28)), Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                var facts = new List<(string Label, string Value, Color Color, float Size)>
                {
                    ("Object", $"{r.ObjectName}" + (string.IsNullOrEmpty(r.ObjectType) ? "" : $"  ({r.ObjectType})"), Theme.Ink, 11f),
                };
                if (!string.IsNullOrEmpty(r.Story)) facts.Add(("Story", r.Story, Theme.Ink, 11f));
                if (!string.IsNullOrEmpty(r.FieldName)) facts.Add(("Field", r.FieldName, Theme.Ink, 11f));
                if (r.ExpectedValue != null) facts.Add(("Expected", r.ExpectedValue, Theme.Ink, 13f));
                if (r.ActualValue != null)
                {
                    var bad = r.Status == ValidationStatus.Fail || r.Status == ValidationStatus.Warning;
                    facts.Add(("Actual in ETABS", r.ActualValue, bad ? Theme.Shade(Theme.ColorForStatus(r.Status), 0.1f) : Theme.Ink, 13f));
                }

                var fx = x;
                var labelY = this.S(52);
                var valueY = this.S(70);
                foreach (var fact in facts)
                {
                    using (var f = fact.Size > 12 ? Theme.DisplayStrong(fact.Size) : Theme.Display(fact.Size))
                    {
                        var w = System.Math.Min(this.S(320), System.Math.Max(this.S(90), TextRenderer.MeasureText(g, fact.Value ?? "", f).Width + this.S(8)));
                        if (fx + w > Width - x) w = Width - x - fx;
                        if (w < this.S(40)) break;
                        TextRenderer.DrawText(g, fact.Label, Theme.Small, new Rectangle(fx, labelY, w, this.S(16)), Theme.InkMuted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, fact.Value ?? "", f, new Rectangle(fx, valueY, w, this.S(30)), fact.Color, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                        fx += w + this.S(28);
                    }
                }
            }
        }
    }
}
