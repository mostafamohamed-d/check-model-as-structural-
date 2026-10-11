using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>
    /// One tab per model-object check (wall geometry, column plumb, ...): the same filters, grid and
    /// detail view as the Results tab, pinned to a single rule, plus a button that puts that check's
    /// failing objects into its ETABS group so they can be selected in the model.
    /// </summary>
    public sealed class CheckTabPage : UserControl
    {
        private readonly FilterToolbar _filters;
        private readonly ResultsGridView _grid;
        private readonly ResultDetailPanel _detail;
        private readonly Label _summary;
        private readonly Button _groupButton;
        private int _failCount;
        private bool _canWrite;

        public string RuleId { get; }
        public int FailCount => _failCount;

        /// <summary>Raised when the user asks for this check's failures to be grouped in ETABS.</summary>
        public event EventHandler GroupRequested;

        public CheckTabPage(string ruleId)
        {
            RuleId = ruleId;
            BackColor = Theme.Surface;

            var rule = RuleRegistry.AllRules.First(r => r.RuleId == ruleId);
            var header = new Panel { Dock = DockStyle.Top, Height = this.S(58), BackColor = Theme.Surface, Padding = new Padding(this.S(20), this.S(10), this.S(16), 0) };
            var title = new Label { Text = rule.Name, Dock = DockStyle.Top, Height = this.S(22), Font = Theme.DisplayStrong(11f), ForeColor = Theme.Ink, AutoEllipsis = true };
            _summary = new Label { Text = $"{ruleId}  ·  ETABS group {FailureGroups.For(ruleId)}  ·  run a validation to see results", Dock = DockStyle.Top, Height = this.S(20), Font = Theme.Small, ForeColor = Theme.InkMuted, AutoEllipsis = true };

            _groupButton = new Button
            {
                Text = "Group failures in ETABS...", AutoSize = true, Dock = DockStyle.Right, Enabled = false,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Accent, Font = Theme.BodyStrong,
                Padding = new Padding(this.S(8), 0, this.S(8), 0), Cursor = Cursors.Hand
            };
            _groupButton.FlatAppearance.BorderColor = Theme.Line;
            _groupButton.Click += (s, e) => GroupRequested?.Invoke(this, EventArgs.Empty);
            var buttonHost = new Panel { Dock = DockStyle.Right, Width = this.S(210), Padding = new Padding(0, this.S(4), 0, this.S(10)), BackColor = Theme.Surface };
            buttonHost.Controls.Add(_groupButton);

            header.Controls.Add(_summary);
            header.Controls.Add(title);
            header.Controls.Add(buttonHost);

            _filters = new FilterToolbar();
            _filters.FilterChanged += (s, f) => Apply(f);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Line, SplitterWidth = this.S(1) + 4 };
            split.Panel1.BackColor = Theme.Surface;
            split.Panel2.BackColor = Theme.Surface;
            _grid = new ResultsGridView();
            _detail = new ResultDetailPanel();
            _grid.SelectionChanged += (s, r) => _detail.SetResult(r);
            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_detail);
            // Hidden tabs are laid out at height 0, so place the splitter the first time the tab has a real size.
            var placed = false;
            EventHandler place = (s, e) =>
            {
                if (placed || !Visible || split.Height < this.S(200)) return;
                placed = true;
                split.SplitterDistance = (int)(split.Height * 0.55);
            };
            split.SizeChanged += place;
            VisibleChanged += place;

            Controls.Add(split);
            Controls.Add(_filters);
            Controls.Add(header);
            Apply(_filters.CurrentFilter);
        }

        private void Apply(ResultFilter filter)
        {
            filter.RuleId = RuleId;
            _grid.ApplyFilter(filter);
        }

        public void SetRun(IReadOnlyList<ValidationResult> allResults, ValidationProfileConfig config, bool canWriteGroups)
        {
            var mine = allResults.Where(r => r.RuleId == RuleId).ToList();
            var summary = ValidationSummary.From(mine);
            _failCount = mine.Count(r => r.Status == ValidationStatus.Fail);
            _canWrite = canWriteGroups && mine.Any(r => r.Status != ValidationStatus.NotChecked);

            _detail.SetConfig(config);
            _filters.SetCounts(summary);
            _filters.SetAvailableCategories(mine.Select(r => r.Category));
            _grid.SetResults(mine);
            Apply(_filters.CurrentFilter);

            var objects = mine.Where(r => r.Status == ValidationStatus.Fail && r.Objects != null).Sum(r => r.Objects.Count);
            _summary.Text = mine.Count == 0
                ? $"{RuleId}  ·  not run (its category is switched off in the profile)"
                : $"{RuleId}  ·  {mine.Count(r => r.Status == ValidationStatus.Pass):N0} pass, {_failCount:N0} fail, " +
                  $"{mine.Count(r => r.Status == ValidationStatus.Warning):N0} warning, {mine.Count(r => r.Status == ValidationStatus.NotChecked):N0} not checked" +
                  $"  ·  {objects:N0} failing object(s) for ETABS group {FailureGroups.For(RuleId)}";
            SetBusy(false);
        }

        /// <summary>Grouping needs a live run with this check in it; an all-pass run is still allowed so the old group gets emptied.</summary>
        public void SetBusy(bool busy) => _groupButton.Enabled = !busy && _canWrite;

        public void DisableGrouping()
        {
            _canWrite = false;
            SetBusy(false);
        }
    }
}
