using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Renders a ValidationSummary as a row of count tiles plus a per-category
    /// breakdown grid - a thin view over data ValidationSummary.From already computes, no new
    /// aggregation logic here.</summary>
    public sealed class SummaryPanel : UserControl
    {
        private readonly FlowLayoutPanel _tiles;
        private readonly DataGridView _categoryGrid;
        private readonly Label _overallLabel;

        public SummaryPanel()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            Padding = new Padding(8);

            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };

            _overallLabel = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            layout.Controls.Add(_overallLabel, 0, 0);

            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            layout.Controls.Add(_tiles, 0, 1);

            _categoryGrid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 160,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Margin = new Padding(0, 6, 0, 0)
            };
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Category", DataPropertyName = "Category", Width = 160 });
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pass", DataPropertyName = "PassCount", Width = 70 });
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Warning", DataPropertyName = "WarningCount", Width = 70 });
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fail", DataPropertyName = "FailCount", Width = 70 });
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Exempt", DataPropertyName = "ExemptCount", Width = 70 });
            _categoryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Not Checked", DataPropertyName = "NotCheckedCount", Width = 90 });
            layout.Controls.Add(_categoryGrid, 0, 2);

            Controls.Add(layout);
        }

        public void SetSummary(ValidationSummary summary)
        {
            _overallLabel.Text = $"Overall Model Status: {summary.OverallStatus}";
            _overallLabel.ForeColor = ColorForStatus(summary.OverallStatus);

            _tiles.Controls.Clear();
            _tiles.Controls.Add(Tile("PASS", summary.PassCount, ColorForStatus(ValidationStatus.Pass)));
            _tiles.Controls.Add(Tile("WARNING", summary.WarningCount, ColorForStatus(ValidationStatus.Warning)));
            _tiles.Controls.Add(Tile("FAIL", summary.FailCount, ColorForStatus(ValidationStatus.Fail)));
            _tiles.Controls.Add(Tile("EXEMPT", summary.ExemptCount, ColorForStatus(ValidationStatus.Exempt)));
            _tiles.Controls.Add(Tile("NOT CHECKED", summary.NotCheckedCount, ColorForStatus(ValidationStatus.NotChecked)));
            _tiles.Controls.Add(Tile("TOTAL", summary.TotalCount, Color.Black));

            _categoryGrid.DataSource = summary.ByCategory.Values.OrderBy(c => c.Category).ToList();
        }

        public static Color ColorForStatus(ValidationStatus status)
        {
            switch (status)
            {
                case ValidationStatus.Pass: return Color.FromArgb(0, 128, 0);
                case ValidationStatus.Warning: return Color.FromArgb(180, 140, 0);
                case ValidationStatus.Fail: return Color.FromArgb(180, 0, 0);
                case ValidationStatus.Exempt: return Color.FromArgb(80, 80, 80);
                default: return Color.FromArgb(90, 90, 160);
            }
        }

        private static Control Tile(string label, int count, Color color)
        {
            var panel = new Panel { Width = 130, Height = 64, Margin = new Padding(0, 0, 10, 0), BorderStyle = BorderStyle.FixedSingle };
            var countLabel = new Label
            {
                Text = count.ToString("N0"),
                Font = new Font(panel.Font.FontFamily, 18, FontStyle.Bold),
                ForeColor = color,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter
            };
            var nameLabel = new Label { Text = label, Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleCenter };
            panel.Controls.Add(countLabel);
            panel.Controls.Add(nameLabel);
            return panel;
        }
    }
}
