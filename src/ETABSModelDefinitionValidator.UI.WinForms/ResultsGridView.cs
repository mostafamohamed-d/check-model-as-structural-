using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>
    /// Displays a (potentially 20,000+ row) result set in a DataGridView running in Virtual
    /// Mode. Virtual mode is what keeps this responsive at that scale: a normal data-bound grid
    /// allocates a real DataGridViewRow/Cell object per row, which is the actual scaling cliff -
    /// virtual mode instead answers CellValueNeeded directly from a plain in-memory list, and
    /// filtering/sorting rebuild that list rather than touching the grid's own (DataTable-based)
    /// filter/sort machinery.
    /// </summary>
    public sealed class ResultsGridView : UserControl
    {
        private static readonly string[] ColumnNames =
        {
            "Status", "RuleId", "Category", "Severity", "ObjectType", "ObjectName",
            "Story", "FieldName", "Expected", "Actual", "Message", "Table", "Row"
        };
        private static readonly string[] Headers =
        {
            "Status", "Rule", "Category", "Severity", "Object type", "Object",
            "Story", "Field", "Expected", "Actual", "Message", "ETABS table", "Row"
        };

        private readonly DataGridView _grid;
        private List<ValidationResult> _allResults = new List<ValidationResult>();
        private List<ValidationResult> _view = new List<ValidationResult>();
        private ResultFilter _lastFilter = new ResultFilter();
        private string _sortColumn;
        private bool _sortAscending = true;

        public event EventHandler<ValidationResult> SelectionChanged;

        public int VisibleCount => _view.Count;
        public int TotalCount => _allResults.Count;

        public ResultsGridView()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Surface;

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                VirtualMode = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                EditMode = DataGridViewEditMode.EditProgrammatically
            };
            Theme.StyleGrid(_grid);

            var widths = new[] { 118, 100, 120, 75, 95, 150, 90, 120, 120, 120, 420, 200, 50 };
            for (var i = 0; i < ColumnNames.Length; i++)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = ColumnNames[i],
                    HeaderText = Headers[i],
                    Width = this.S(widths[i]),
                    SortMode = DataGridViewColumnSortMode.Programmatic
                });
            }
            _grid.Columns["Message"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["Message"].MinimumWidth = this.S(240);

            _grid.CellValueNeeded += Grid_CellValueNeeded;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellPainting += Grid_CellPainting;
            _grid.SelectionChanged += Grid_SelectionChanged;
            _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;

            Controls.Add(_grid);
        }

        public void SetResults(IReadOnlyList<ValidationResult> results)
        {
            _allResults = results.ToList();
            ApplyFilter(_lastFilter);
        }

        public void ApplyFilter(ResultFilter filter)
        {
            _lastFilter = filter;
            IEnumerable<ValidationResult> filtered = _allResults.Where(filter.Matches);
            _view = _sortColumn == null ? filtered.ToList() : Sort(filtered, _sortColumn, _sortAscending).ToList();

            _grid.RowCount = 0;
            _grid.RowCount = _view.Count;
            _grid.Invalidate();
            if (_view.Count == 0) SelectionChanged?.Invoke(this, null);
        }

        public List<ValidationResult> CurrentView => _view;

        private static IEnumerable<ValidationResult> Sort(IEnumerable<ValidationResult> source, string column, bool ascending)
        {
            Comparison<ValidationResult> cmp;
            switch (column)
            {
                case "Status": cmp = (a, b) => a.Status.CompareTo(b.Status); break;
                case "Severity": cmp = (a, b) => a.Severity.CompareTo(b.Severity); break;
                case "Row": cmp = (a, b) => (a.Source?.RowNumber ?? 0).CompareTo(b.Source?.RowNumber ?? 0); break;
                case "RuleId": cmp = (a, b) => string.CompareOrdinal(a.RuleId, b.RuleId); break;
                case "Category": cmp = (a, b) => string.CompareOrdinal(a.Category, b.Category); break;
                case "ObjectType": cmp = (a, b) => string.CompareOrdinal(a.ObjectType, b.ObjectType); break;
                case "ObjectName": cmp = (a, b) => string.CompareOrdinal(a.ObjectName, b.ObjectName); break;
                case "Story": cmp = (a, b) => string.CompareOrdinal(a.Story, b.Story); break;
                case "FieldName": cmp = (a, b) => string.CompareOrdinal(a.FieldName, b.FieldName); break;
                case "Expected": cmp = (a, b) => string.CompareOrdinal(a.ExpectedValue, b.ExpectedValue); break;
                case "Actual": cmp = (a, b) => string.CompareOrdinal(a.ActualValue, b.ActualValue); break;
                case "Message": cmp = (a, b) => string.CompareOrdinal(a.Message, b.Message); break;
                case "Table": cmp = (a, b) => string.CompareOrdinal(a.Source?.TableName, b.Source?.TableName); break;
                default: cmp = (a, b) => 0; break;
            }

            var list = source.ToList();
            list.Sort((a, b) => ascending ? cmp(a, b) : cmp(b, a));
            return list;
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var column = ColumnNames[e.ColumnIndex];
            _sortAscending = _sortColumn == column ? !_sortAscending : true;
            _sortColumn = column;
            foreach (DataGridViewColumn c in _grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
            _grid.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection = _sortAscending ? SortOrder.Ascending : SortOrder.Descending;
            ApplyFilter(_lastFilter);
        }

        private void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var r = _view[e.RowIndex];

            switch (ColumnNames[e.ColumnIndex])
            {
                case "Status": e.Value = Theme.StatusText(r.Status); break;
                case "RuleId": e.Value = r.RuleId; break;
                case "Category": e.Value = r.Category; break;
                case "Severity": e.Value = r.Severity.ToString(); break;
                case "ObjectType": e.Value = r.ObjectType; break;
                case "ObjectName": e.Value = r.ObjectName; break;
                case "Story": e.Value = r.Story; break;
                case "FieldName": e.Value = r.FieldName; break;
                case "Expected": e.Value = r.ExpectedValue; break;
                case "Actual": e.Value = r.ActualValue; break;
                case "Message": e.Value = r.Message; break;
                case "Table": e.Value = r.Source?.TableName; break;
                case "Row": e.Value = r.Source?.RowNumber; break;
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Virtual mode only calls this for cells actually being painted (the viewport), so
            // this stays cheap regardless of total row count.
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var r = _view[e.RowIndex];
            switch (ColumnNames[e.ColumnIndex])
            {
                case "RuleId": e.CellStyle.Font = Theme.BodyStrong; break;
                case "Severity":
                    if (r.Severity == Severity.Critical) { e.CellStyle.ForeColor = Theme.Fail; e.CellStyle.SelectionForeColor = Theme.Fail; }
                    else if (r.Severity == Severity.Low) e.CellStyle.ForeColor = Theme.InkMuted;
                    break;
                case "Actual":
                    if (r.Status == ValidationStatus.Fail || r.Status == ValidationStatus.Warning)
                    {
                        var c = Theme.Shade(Theme.ColorForStatus(r.Status), 0.1f);
                        e.CellStyle.ForeColor = c;
                        e.CellStyle.SelectionForeColor = c;
                        e.CellStyle.Font = Theme.BodyStrong;
                    }
                    break;
                case "Category": case "ObjectType": case "Table": case "Row": case "Story":
                    e.CellStyle.ForeColor = Theme.InkMuted;
                    break;
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // Only visible cells are painted, so pills cost nothing at 20k rows.
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count || ColumnNames[e.ColumnIndex] != "Status") return;
            e.PaintBackground(e.ClipBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            Theme.DrawStatusPill(e.Graphics, e.CellBounds, _view[e.RowIndex].Status, _grid);
            e.Handled = true;
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            var rowIndex = _grid.CurrentCell?.RowIndex ?? -1;
            var selected = rowIndex >= 0 && rowIndex < _view.Count ? _view[rowIndex] : null;
            SelectionChanged?.Invoke(this, selected);
        }
    }
}
