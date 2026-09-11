using System;
using System.Linq;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Status/Category/Severity/free-text filter controls. Raises FilterChanged with the
    /// current ResultFilter whenever a control changes; free-text is debounced with a short timer
    /// so fast typing doesn't refilter on every keystroke.</summary>
    public sealed class FilterToolbar : UserControl
    {
        private readonly ComboBox _statusCombo;
        private readonly ComboBox _categoryCombo;
        private readonly ComboBox _severityCombo;
        private readonly TextBox _searchBox;
        private readonly Timer _debounce;

        public event EventHandler<ResultFilter> FilterChanged;

        public FilterToolbar()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);

            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };

            _statusCombo = LabeledCombo(flow, "Status:", new object[] { "(any)" }.Concat(Enum.GetNames(typeof(ValidationStatus))).ToArray());
            _categoryCombo = LabeledCombo(flow, "Category:", new object[] { "(any)" });
            _severityCombo = LabeledCombo(flow, "Severity:", new object[] { "(any)" }.Concat(Enum.GetNames(typeof(Severity))).ToArray());

            flow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new System.Windows.Forms.Padding(10, 8, 4, 0) });
            _searchBox = new TextBox { Width = 220, Margin = new System.Windows.Forms.Padding(0, 4, 0, 0) };
            flow.Controls.Add(_searchBox);

            Controls.Add(flow);

            _debounce = new Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); RaiseFilterChanged(); };

            _statusCombo.SelectedIndexChanged += (s, e) => RaiseFilterChanged();
            _categoryCombo.SelectedIndexChanged += (s, e) => RaiseFilterChanged();
            _severityCombo.SelectedIndexChanged += (s, e) => RaiseFilterChanged();
            _searchBox.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
        }

        private static ComboBox LabeledCombo(FlowLayoutPanel flow, string label, object[] items)
        {
            flow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 8, 4, 0) });
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new System.Windows.Forms.Padding(0, 4, 0, 0) };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            flow.Controls.Add(combo);
            return combo;
        }

        /// <summary>Populate the Category dropdown from the actual result set - keeps it in sync
        /// with whatever categories the current run actually produced.</summary>
        public void SetAvailableCategories(System.Collections.Generic.IEnumerable<string> categories)
        {
            var current = _categoryCombo.SelectedItem as string;
            _categoryCombo.Items.Clear();
            _categoryCombo.Items.Add("(any)");
            foreach (var c in categories.Distinct().OrderBy(c => c)) _categoryCombo.Items.Add(c);
            _categoryCombo.SelectedItem = current != null && _categoryCombo.Items.Contains(current) ? current : "(any)";
        }

        public ResultFilter CurrentFilter => new ResultFilter
        {
            Status = _statusCombo.SelectedIndex > 0 ? (ValidationStatus?)Enum.Parse(typeof(ValidationStatus), (string)_statusCombo.SelectedItem) : null,
            Category = _categoryCombo.SelectedIndex > 0 ? (string)_categoryCombo.SelectedItem : null,
            Severity = _severityCombo.SelectedIndex > 0 ? (Severity?)Enum.Parse(typeof(Severity), (string)_severityCombo.SelectedItem) : null,
            SearchText = _searchBox.Text
        };

        private void RaiseFilterChanged() => FilterChanged?.Invoke(this, CurrentFilter);
    }
}
