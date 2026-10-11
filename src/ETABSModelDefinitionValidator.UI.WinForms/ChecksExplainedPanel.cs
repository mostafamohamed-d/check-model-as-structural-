using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Lists every check the tool can perform, what it does and why, and (after a run)
    /// how many objects passed/failed it - so an engineer can see exactly what was and was not
    /// verified, including checks that returned NOT CHECKED.</summary>
    public sealed class ChecksExplainedPanel : UserControl
    {
        private sealed class Row
        {
            public string Id { get; set; }
            public string Check { get; set; }
            public string Basis { get; set; }
            public int Pass { get; set; }
            public int Warning { get; set; }
            public int Fail { get; set; }
            public int Exempt { get; set; }
            public int NotChecked { get; set; }
            public string Outcome { get; set; }
            public ValidationStatus? Status { get; set; }
            public RuleExplanation Explanation { get; set; }
        }

        private readonly DataGridView _grid;
        private readonly RichTextBox _box;
        private List<Row> _rows = new List<Row>();

        public ChecksExplainedPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Base;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = this.S(6), BackColor = Theme.Base };
            split.Panel1.BackColor = Theme.Surface;
            split.Panel2.BackColor = Theme.Surface;
            split.SizeChanged += (s, e) =>
            {
                var d = (int)(split.Width * 0.66);
                if (d > split.Panel1MinSize && d < split.Width - split.Panel2MinSize) split.SplitterDistance = d;
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
            };
            Theme.StyleGrid(_grid);
            AddColumn("Outcome", "Outcome", 112);
            AddColumn("Id", "Rule", 118);
            AddColumn("Check", "Check", 250).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddColumn("Basis", "Basis", 140);
            AddColumn("Fail", "Fail", 56);
            AddColumn("Warning", "Warn", 56);
            AddColumn("NotChecked", "Not checked", 106);
            AddColumn("Exempt", "Exempt", 78);
            AddColumn("Pass", "Pass", 56);
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellPainting += Grid_CellPainting;
            _grid.SelectionChanged += (s, e) => ShowSelected();
            split.Panel1.Controls.Add(_grid);

            split.Panel2.Padding = new Padding(this.S(20), this.S(16), this.S(16), this.S(8));
            _box = ExplanationWriter.NewBox();
            split.Panel2.Controls.Add(_box);
            Controls.Add(split);
        }

        private DataGridViewColumn AddColumn(string prop, string header, int width)
        {
            // Bound to a plain List, which cannot sort - so no space is reserved for a sort glyph.
            var c = new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header, Width = this.S(width), SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(c);
            return c;
        }

        /// <summary>Rebuilds the list from the profile and (optionally) a finished run.</summary>
        public void SetData(ValidationProfileConfig config, IReadOnlyList<ValidationResult> results)
        {
            var byRule = (results ?? new List<ValidationResult>()).GroupBy(r => r.RuleId).ToDictionary(g => g.Key, g => g.ToList());
            var ran = results != null;

            _rows = RuleCatalog.All(config).Select(e =>
            {
                byRule.TryGetValue(e.RuleId, out var list);
                list = list ?? new List<ValidationResult>();
                var row = new Row
                {
                    Id = e.RuleId, Check = e.Title, Basis = ExplanationWriter.TypeLabel(e.Type), Explanation = e,
                    Pass = list.Count(r => r.Status == ValidationStatus.Pass),
                    Warning = list.Count(r => r.Status == ValidationStatus.Warning),
                    Fail = list.Count(r => r.Status == ValidationStatus.Fail),
                    Exempt = list.Count(r => r.Status == ValidationStatus.Exempt),
                    NotChecked = list.Count(r => r.Status == ValidationStatus.NotChecked)
                };
                row.Status = !ran || list.Count == 0 ? (ValidationStatus?)null
                    : row.Fail > 0 ? ValidationStatus.Fail : row.Warning > 0 ? ValidationStatus.Warning
                    : row.Pass > 0 ? ValidationStatus.Pass : row.NotChecked > 0 ? ValidationStatus.NotChecked : ValidationStatus.Exempt;
                row.Outcome = !ran ? "" : row.Status == null ? "Not run" : Theme.StatusText(row.Status.Value);
                return row;
            }).ToList();

            _grid.DataSource = _rows;
            ShowSelected();
        }

        private void ShowSelected()
        {
            _box.Clear();
            var row = _grid.CurrentRow?.DataBoundItem as Row;
            if (row == null) return;
            var e = row.Explanation;
            ExplanationWriter.Title(_box, $"{e.RuleId}  {e.Title}", Theme.Ink);
            ExplanationWriter.Section(_box, "Category", e.Category);
            ExplanationWriter.Explanation(_box, e);
            _box.SelectionStart = 0;
            _box.ScrollToCaret();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            var row = _rows[e.RowIndex];
            switch (_grid.Columns[e.ColumnIndex].DataPropertyName)
            {
                case "Id": e.CellStyle.Font = Theme.BodyStrong; break;
                case "Basis": case "Outcome": e.CellStyle.ForeColor = Theme.InkMuted; break;
                case "Fail": Count(e, row.Fail, Theme.Fail); break;
                case "Warning": Count(e, row.Warning, Theme.Shade(Theme.Warning)); break;
                case "NotChecked": Count(e, row.NotChecked, Theme.NotChecked); break;
                case "Exempt": Count(e, row.Exempt, Theme.InkMuted); break;
                case "Pass": Count(e, row.Pass, Theme.InkMuted); break;
            }
        }

        /// <summary>Zero counts fade out so the non-zero ones stand out down each column.</summary>
        private static void Count(DataGridViewCellFormattingEventArgs e, int n, Color color)
        {
            var c = n == 0 ? Theme.Line : color;
            e.CellStyle.ForeColor = c;
            e.CellStyle.SelectionForeColor = n == 0 ? Theme.InkMuted : c;
            if (n > 0 && color != Theme.InkMuted) e.CellStyle.Font = Theme.BodyStrong;
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || _grid.Columns[e.ColumnIndex].DataPropertyName != "Outcome") return;
            var status = _rows[e.RowIndex].Status;
            if (status == null) return;
            e.PaintBackground(e.ClipBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            Theme.DrawStatusPill(e.Graphics, e.CellBounds, status.Value, _grid);
            e.Handled = true;
        }
    }
}
