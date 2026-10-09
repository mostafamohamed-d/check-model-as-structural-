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
        private readonly ComboBox _instanceCombo;
        private readonly Button _connectButton;
        private readonly Label _connectionLabel;
        private readonly TextBox _configPathBox;
        private readonly CheckedListBox _categoryList;
        private readonly Button _runButton;
        private readonly Button _saveTablesButton;
        private int? _connectedPid;
        private readonly TextBox _importLog;
        private readonly TabControl _tabs;
        private readonly TabPage _logTab;
        private readonly Button _cancelButton;
        private readonly Button _exportButton;
        private readonly ProgressBar _progressBar;
        private readonly SummaryPanel _summaryPanel;
        private readonly FilterToolbar _filterToolbar;
        private readonly ResultsGridView _resultsGrid;
        private readonly ResultDetailPanel _detailPanel;
        private readonly ChecksExplainedPanel _checksPanel;

        private readonly ValidationRunner _runner = new ValidationRunner();
        private CancellationTokenSource _cts;
        private ValidationRunResult _lastRun;

        public MainForm()
        {
            Text = "ETABS Model Definition Validator";
            Width = 1300;
            Height = 850;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            BackColor = System.Drawing.Color.FromArgb(245, 246, 248);

            var header = new Label
            {
                Text = "ETABS Model Definition Validator   |   QA/QC of model definitions against project and code rules",
                Dock = DockStyle.Top, Height = 38, ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(31, 56, 100),
                Font = new System.Drawing.Font("Segoe UI Semibold", 11f),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0)
            };

            // --- ETABS connection row ---
            var filePanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(8) };
            filePanel.Controls.Add(new Label { Text = "Running ETABS:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _instanceCombo = new ComboBox { Width = 500, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left };
            filePanel.Controls.Add(_instanceCombo, 1, 0);
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (s, e) => RefreshInstances();
            filePanel.Controls.Add(refresh, 2, 0);
            _connectButton = new Button { Text = "Connect", AutoSize = true };
            _connectButton.Click += ConnectButton_Click;
            filePanel.Controls.Add(_connectButton, 3, 0);
            _connectionLabel = new Label { Text = "Not connected.", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = System.Drawing.Color.FromArgb(180, 0, 0) };
            filePanel.Controls.Add(_connectionLabel, 4, 0);

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

            _runButton = new Button { Text = "Run Validation", Width = 140, Height = 32, Location = new System.Drawing.Point(260, 24), Enabled = false };
            _runButton.Click += (s, e) => RunValidation();
            _saveTablesButton = new Button { Text = "Save Extracted Tables...", Width = 170, Height = 32, Location = new System.Drawing.Point(632, 24), Enabled = false };
            _saveTablesButton.Click += SaveTablesButton_Click;
            runPanel.Controls.Add(_saveTablesButton);
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
            var resultsTab = new TabPage("Results");
            resultsTab.Controls.Add(split);
            resultsTab.Controls.Add(_filterToolbar);
            resultsTab.Controls.Add(_summaryPanel);

            _checksPanel = new ChecksExplainedPanel();
            var checksTab = new TabPage("Checks Explained");
            checksTab.Controls.Add(_checksPanel);
            _checksPanel.SetData(ConfigurationLoader.LoadDefault(), null);

            _importLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new System.Drawing.Font("Consolas", 9f), BackColor = System.Drawing.Color.White
            };
            var logTab = new TabPage("Import Log");
            logTab.Controls.Add(_importLog);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            var tabs = _tabs;
            tabs.TabPages.Add(resultsTab);
            tabs.TabPages.Add(checksTab);
            tabs.TabPages.Add(logTab);
            _logTab = logTab;

            Controls.Add(tabs);
            Controls.Add(runPanel);
            Controls.Add(filePanel);
            Controls.Add(header);
            Controls.Add(statusStrip);

            RefreshInstances();
        }

        private sealed class EtabsInstance
        {
            public int ProcessId;
            public string Text;
            public override string ToString() => Text;
        }

        private void RefreshInstances()
        {
            _instanceCombo.Items.Clear();
            foreach (var p in ETABSModelDefinitionValidator.EtabsApi.EtabsSession.FindRunningEtabs())
            {
                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? "(no window title)" : p.MainWindowTitle;
                _instanceCombo.Items.Add(new EtabsInstance { ProcessId = p.Id, Text = $"{title}   [pid {p.Id}]" });
            }
            if (_instanceCombo.Items.Count > 0) _instanceCombo.SelectedIndex = 0;
            _statusToolLabel.Text = _instanceCombo.Items.Count == 0
                ? "No running ETABS found. Open ETABS with your model, then click Refresh."
                : $"{_instanceCombo.Items.Count} running ETABS instance(s) found. Select one and click Connect.";
        }

        private async void ConnectButton_Click(object sender, EventArgs e)
        {
            var instance = _instanceCombo.SelectedItem as EtabsInstance;
            if (instance == null)
            {
                MessageBox.Show(this, "No running ETABS selected. Open ETABS with your model, then click Refresh.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _connectButton.Enabled = false;
            _statusToolLabel.Text = $"Connecting to ETABS (pid {instance.ProcessId})...";
            try
            {
                var session = await System.Threading.Tasks.Task.Run(() => ETABSModelDefinitionValidator.EtabsApi.EtabsSession.Attach(instance.ProcessId));
                _connectedPid = session.ProcessId;
                _connectionLabel.Text = $"Connected: {session.ModelName}  ({System.IO.Path.GetFileName(session.EtabsFolder)})";
                _connectionLabel.ForeColor = System.Drawing.Color.FromArgb(0, 128, 0);
                _runButton.Enabled = true;
                _statusToolLabel.Text = "Connected. Choose categories and click Run Validation.";
            }
            catch (Exception ex)
            {
                _connectedPid = null;
                _connectionLabel.Text = "Not connected.";
                _connectionLabel.ForeColor = System.Drawing.Color.FromArgb(180, 0, 0);
                _runButton.Enabled = false;
                _statusToolLabel.Text = "Connection failed.";
                MessageBox.Show(this, ex.Message, "Could not connect to ETABS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _connectButton.Enabled = true;
            }
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

        private async void RunValidation()
        {
            if (_connectedPid == null)
            {
                MessageBox.Show(this, "Connect to a running ETABS first.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _lastRun = await _runner.RunLiveAsync(config, enabledCategories, progress, _cts.Token, _connectedPid);

                _detailPanel.SetConfig(config);
                _checksPanel.SetData(config, _lastRun.Results);
                _summaryPanel.SetSummary(ValidationSummary.From(_lastRun.Results));
                _filterToolbar.SetAvailableCategories(_lastRun.Results.Select(r => r.Category));
                _resultsGrid.SetResults(_lastRun.Results);
                UpdateStatusCounts();
                _exportButton.Enabled = true;
                _saveTablesButton.Enabled = _lastRun.ExtractedTables != null;

                _importLog.Text = BuildImportLog(_lastRun);
                var problems = _lastRun.Model.ImportIssues.Count(i => i.Level != ETABSModelDefinitionValidator.Core.Model.ImportIssueLevel.Info);
                if (problems > 0)
                {
                    _statusToolLabel.Text += $"  ({problems} import problem(s) - see the Import Log tab)";
                    if (_lastRun.Results.Count == 0 || (_lastRun.ExtractedTables?.Count ?? 0) == 0) _tabs.SelectedTab = _logTab;
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

        private static string BuildImportLog(ValidationRunResult run)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Source: {run.Model.WorkbookName}");
            sb.AppendLine();
            sb.AppendLine("TABLES READ FROM ETABS");
            if (run.ExtractedTables == null || run.ExtractedTables.Count == 0) sb.AppendLine("  (none)");
            else foreach (var t in run.ExtractedTables)
            {
                sb.AppendLine($"  {t.TableTitle}  -  {t.Rows.Count} record(s)");
                sb.AppendLine($"      columns: {string.Join(" | ", t.Columns.Select(c => c.OriginalHeader))}");
            }

            sb.AppendLine();
            sb.AppendLine("MISSING TABLES (their checks show NOT CHECKED)");
            if (run.Model.MissingTables.Count == 0) sb.AppendLine("  (none)");
            else foreach (var m in run.Model.MissingTables) sb.AppendLine("  " + m);

            sb.AppendLine();
            sb.AppendLine("MESSAGES");
            if (run.Model.ImportIssues.Count == 0) sb.AppendLine("  (none)");
            else foreach (var i in run.Model.ImportIssues) sb.AppendLine($"  [{i.Level}] {i.Message}");

            if (run.EtabsTables != null && run.EtabsTables.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"ALL TABLES ETABS OFFERS FOR THIS MODEL ({run.EtabsTables.Count})");
                foreach (var t in run.EtabsTables.OrderBy(t => t.Name)) sb.AppendLine("  " + t.Name);
            }

            return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        private void SetRunning(bool running)
        {
            _runButton.Enabled = !running && _connectedPid != null;
            _connectButton.Enabled = !running;
            _cancelButton.Enabled = running;
            _progressBar.MarqueeAnimationSpeed = running ? 30 : 0;
            if (!running && _lastRun == null) _statusToolLabel.Text = "Ready.";
        }

        private void UpdateStatusCounts()
        {
            _statusToolLabel.Text = $"Showing {_resultsGrid.VisibleCount:N0} of {_resultsGrid.TotalCount:N0} result(s).";
        }

        private void SaveTablesButton_Click(object sender, EventArgs e)
        {
            if (_lastRun?.ExtractedTables == null) return;
            using (var dialog = new FolderBrowserDialog { Description = "Folder for the tables read from ETABS (one CSV per table)" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var files = ETABSModelDefinitionValidator.EtabsApi.ExtractedTableWriter.WriteAll(dialog.SelectedPath, _lastRun.ExtractedTables, _lastRun.EtabsTables);
                    MessageBox.Show(this, $"Saved {files.Count} file(s) to:\n{dialog.SelectedPath}", "Tables saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not save tables: {ex.Message}", "Save error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
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
