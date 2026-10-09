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
            public RuleExplanation Explanation { get; set; }
        }

        private readonly DataGridView _grid;
        private readonly RichTextBox _box;
        private List<Row> _rows = new List<Row>();

        public ChecksExplainedPanel()
        {
            Dock = DockStyle.Fill;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None
            };
            AddColumn("Id", "Rule", 95);
            AddColumn("Check", "Check", 250);
            AddColumn("Basis", "Basis", 130);
            AddColumn("Pass", "Pass", 45);
            AddColumn("Warning", "Warn", 45);
            AddColumn("Fail", "Fail", 40);
            AddColumn("Exempt", "Exempt", 55);
            AddColumn("NotChecked", "Not chk.", 60);
            AddColumn("Outcome", "Outcome", 90);
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.SelectionChanged += (s, e) => ShowSelected();
            split.Panel1.Controls.Add(_grid);

            split.Panel2.Padding = new Padding(8);
            _box = ExplanationWriter.NewBox();
            split.Panel2.Controls.Add(_box);
            Controls.Add(split);
        }

        private void AddColumn(string prop, string header, int width) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header, Width = width });

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
                row.Outcome = !ran ? "" : list.Count == 0 ? "Not run" : row.Fail > 0 ? "FAIL" : row.Warning > 0 ? "WARNING"
                    : row.Pass > 0 ? "PASS" : row.NotChecked > 0 ? "NOT CHECKED" : "EXEMPT";
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
            ExplanationWriter.Title(_box, $"{e.RuleId}  {e.Title}", Color.FromArgb(40, 70, 120));
            ExplanationWriter.Section(_box, "Category", e.Category);
            ExplanationWriter.Explanation(_box, e);
            _box.SelectionStart = 0;
            _box.ScrollToCaret();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || _grid.Columns[e.ColumnIndex].DataPropertyName != "Outcome") return;
            switch (_rows[e.RowIndex].Outcome)
            {
                case "PASS": e.CellStyle.ForeColor = SummaryPanel.ColorForStatus(ValidationStatus.Pass); break;
                case "WARNING": e.CellStyle.ForeColor = SummaryPanel.ColorForStatus(ValidationStatus.Warning); break;
                case "FAIL": e.CellStyle.ForeColor = SummaryPanel.ColorForStatus(ValidationStatus.Fail); break;
                case "NOT CHECKED": case "Not run": e.CellStyle.ForeColor = SummaryPanel.ColorForStatus(ValidationStatus.NotChecked); break;
            }
        }
    }
}
