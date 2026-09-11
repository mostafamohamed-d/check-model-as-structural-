using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Reporting;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly TextBox _excelPathBox;
        private readonly TextBox _configPathBox;
        private readonly CheckedListBox _categoryList;
        private readonly Button _runButton;
        private readonly Button _cancelButton;
        private readonly Button _exportButton;
        private readonly ProgressBar _progressBar;
        private readonly SummaryPanel _summaryPanel;
        private readonly FilterToolbar _filterToolbar;
        private readonly ResultsGridView _resultsGrid;
        private readonly ResultDetailPanel _detailPanel;

        private readonly ValidationRunner _runner = new ValidationRunner();
        private CancellationTokenSource _cts;
        private ValidationRunResult _lastRun;

        public MainForm()
        {
            Text = "ETABS Model Definition Validator";
            Width = 1300;
            Height = 850;
            StartPosition = FormStartPosition.CenterScreen;

            // --- File picker row ---
            var filePanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8) };
            filePanel.Controls.Add(new Label { Text = "ETABS Excel export:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _excelPathBox = new TextBox { Width = 500, Anchor = AnchorStyles.Left };
            filePanel.Controls.Add(_excelPathBox, 1, 0);
            var browseExcel = new Button { Text = "Browse...", AutoSize = true };
            browseExcel.Click += (s, e) => BrowseFor(_excelPathBox, "Excel files (*.xlsx)|*.xlsx");
            filePanel.Controls.Add(browseExcel, 2, 0);

            filePanel.Controls.Add(new Label { Text = "Config (optional):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _configPathBox = new TextBox { Width = 500, Anchor = AnchorStyles.Left };
            filePanel.Controls.Add(_configPathBox, 1, 1);
            var browseConfig = new Button { Text = "Browse...", AutoSize = true };
            browseConfig.Click += (s, e) => BrowseFor(_configPathBox, "JSON config (*.json)|*.json");
            filePanel.Controls.Add(browseConfig, 2, 1);

            // --- Category picker + run controls ---
            var runPanel = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(8, 0, 8, 8) };

            var categoryLabel = new Label { Text = "Validation profile (categories to run):", AutoSize = true, Location = new System.Drawing.Point(8, 4) };
            _categoryList = new CheckedListBox { Width = 240, Height = 110, CheckOnClick = true, Location = new System.Drawing.Point(8, 24) };
            foreach (var category in RuleRegistry.AllRules.Select(r => r.Category).Distinct().OrderBy(c => c))
            {
                _categoryList.Items.Add(category, true);
            }
            runPanel.Controls.Add(categoryLabel);
            runPanel.Controls.Add(_categoryList);

            _runButton = new Button { Text = "Run Validation", Width = 140, Height = 32, Location = new System.Drawing.Point(260, 24) };
            _runButton.Click += RunButton_Click;
            _cancelButton = new Button { Text = "Cancel", Width = 90, Height = 32, Location = new System.Drawing.Point(408, 24), Enabled = false };
            _cancelButton.Click += (s, e) => _cts?.Cancel();
            _exportButton = new Button { Text = "Export CSV...", Width = 120, Height = 32, Location = new System.Drawing.Point(506, 24), Enabled = false };
            _exportButton.Click += ExportButton_Click;
            runPanel.Controls.Add(_runButton);
            runPanel.Controls.Add(_cancelButton);
            runPanel.Controls.Add(_exportButton);

            // --- Summary + filters ---
            _summaryPanel = new SummaryPanel();
            _filterToolbar = new FilterToolbar();
            _filterToolbar.FilterChanged += (s, filter) =>
            {
                _resultsGrid.ApplyFilter(filter);
                UpdateStatusCounts();
            };

            // --- Results grid + detail split ---
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 480 };
            _resultsGrid = new ResultsGridView();
            _resultsGrid.SelectionChanged += (s, result) => _detailPanel.SetResult(result);
            split.Panel1.Controls.Add(_resultsGrid);
            _detailPanel = new ResultDetailPanel();
            split.Panel2.Controls.Add(_detailPanel);

            // --- Status strip ---
            var statusStrip = new StatusStrip();
            var statusToolLabel = new ToolStripStatusLabel { Text = "Ready.", Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            var progressHost = new ToolStripControlHost(new ProgressBar { Width = 200, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0 });
            _progressBar = (ProgressBar)progressHost.Control;
            statusStrip.Items.Add(statusToolLabel);
            statusStrip.Items.Add(progressHost);
            _statusToolLabel = statusToolLabel;

            // --- Assemble ---
            Controls.Add(split);
            Controls.Add(_filterToolbar);
            Controls.Add(_summaryPanel);
            Controls.Add(runPanel);
            Controls.Add(filePanel);
            Controls.Add(statusStrip);
        }

        private ToolStripStatusLabel _statusToolLabel;

        private static void BrowseFor(TextBox target, string filter)
        {
            using (var dialog = new OpenFileDialog { Filter = filter })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    target.Text = dialog.FileName;
                }
            }
        }

        private async void RunButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_excelPathBox.Text) || !System.IO.File.Exists(_excelPathBox.Text))
            {
                MessageBox.Show(this, "Choose a valid ETABS Excel export first.", "Missing file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var enabledCategories = _categoryList.CheckedItems.Cast<string>().ToList();
            if (enabledCategories.Count == 0)
            {
                MessageBox.Show(this, "Select at least one category to validate.", "Nothing to run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ValidationProfileConfig config;
            try
            {
                config = string.IsNullOrWhiteSpace(_configPathBox.Text)
                    ? ConfigurationLoader.LoadDefault()
                    : ConfigurationLoader.LoadFromFile(_configPathBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not load config: {ex.Message}", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            SetRunning(true);

            var progress = new Progress<string>(msg => _statusToolLabel.Text = msg);

            try
            {
                _lastRun = await _runner.RunAsync(_excelPathBox.Text, config, enabledCategories, progress, _cts.Token);

                _summaryPanel.SetSummary(ValidationSummary.From(_lastRun.Results));
                _filterToolbar.SetAvailableCategories(_lastRun.Results.Select(r => r.Category));
                _resultsGrid.SetResults(_lastRun.Results);
                UpdateStatusCounts();
                _exportButton.Enabled = true;

                if (_lastRun.Model.MissingTables.Count > 0)
                {
                    _statusToolLabel.Text += $"  ({_lastRun.Model.MissingTables.Count} required table(s) missing)";
                }
            }
            catch (OperationCanceledException)
            {
                _statusToolLabel.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                _statusToolLabel.Text = "Failed.";
                MessageBox.Show(this, ex.Message, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRunning(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void SetRunning(bool running)
        {
            _runButton.Enabled = !running;
            _cancelButton.Enabled = running;
            _progressBar.MarqueeAnimationSpeed = running ? 30 : 0;
            if (!running && _lastRun == null) _statusToolLabel.Text = "Ready.";
        }

        private void UpdateStatusCounts()
        {
            _statusToolLabel.Text = $"Showing {_resultsGrid.VisibleCount:N0} of {_resultsGrid.TotalCount:N0} result(s).";
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_lastRun == null) return;

            using (var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "validation-report.csv" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    CsvReportWriter.WriteToFile(_resultsGrid.CurrentView, dialog.FileName);
                    MessageBox.Show(this, $"Exported {_resultsGrid.CurrentView.Count:N0} row(s) to:\n{dialog.FileName}", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Export failed: {ex.Message}", "Export error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
