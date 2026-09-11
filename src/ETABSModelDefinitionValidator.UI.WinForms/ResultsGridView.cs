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

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                VirtualMode = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                EditMode = DataGridViewEditMode.EditProgrammatically
            };

            var widths = new[] { 80, 90, 130, 80, 100, 150, 100, 110, 150, 150, 420, 170, 60 };
            for (var i = 0; i < ColumnNames.Length; i++)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = ColumnNames[i],
                    HeaderText = ColumnNames[i],
                    Width = widths[i],
                    SortMode = DataGridViewColumnSortMode.Programmatic
                });
            }

            _grid.CellValueNeeded += Grid_CellValueNeeded;
            _grid.CellFormatting += Grid_CellFormatting;
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
            ApplyFilter(_lastFilter);
        }

        private void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var r = _view[e.RowIndex];

            switch (ColumnNames[e.ColumnIndex])
            {
                case "Status": e.Value = r.Status.ToString(); break;
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
            e.CellStyle.ForeColor = SummaryPanel.ColorForStatus(_view[e.RowIndex].Status);
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            var rowIndex = _grid.CurrentCell?.RowIndex ?? -1;
            var selected = rowIndex >= 0 && rowIndex < _view.Count ? _view[rowIndex] : null;
            SelectionChanged?.Invoke(this, selected);
        }
    }
}
