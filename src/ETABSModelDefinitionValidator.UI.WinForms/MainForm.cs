using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Reporting;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox _instanceCombo;
        private readonly Button _connectButton;
        private readonly ConnectionBadge _connection;
        private readonly Button _profileButton;
        private readonly ContextMenuStrip _profileMenu;
        private readonly List<ToolStripMenuItem> _categoryItems = new List<ToolStripMenuItem>();
        private readonly ToolStripMenuItem _configLabel;
        private string _configPath;
        private readonly Button _runButton;
        private readonly Button _exportButton;
        private readonly ContextMenuStrip _exportMenu;
        private readonly ToolStripMenuItem _exportCsvItem;
        private readonly ToolStripMenuItem _saveTablesItem;
        private int? _connectedPid;
        private readonly TextBox _importLog;
        private readonly TabStrip _tabs;
        private readonly ActivityLine _activity;
        private readonly Footer _footer;
        private readonly StatusBand _band;
        private readonly FilterToolbar _filterToolbar;
        private readonly ResultsGridView _resultsGrid;
        private readonly ResultDetailPanel _detailPanel;
        private readonly ChecksExplainedPanel _checksPanel;
        private readonly List<CheckTabPage> _checkTabs = new List<CheckTabPage>();
        private readonly ToolStripMenuItem _groupsItem;

        private readonly ValidationRunner _runner = new ValidationRunner();
        private CancellationTokenSource _cts;
        private ValidationRunResult _lastRun;
        private int _importProblems;

        private const int ResultsTab = 0, LogTab = 2, FirstCheckTab = 3;

        /// <summary>The model-object checks that get a tab of their own, in tab order.</summary>
        private static readonly (string RuleId, string Title)[] CheckTabs =
        {
            (RuleIds.Geo001, "Wall geometry"),
            (RuleIds.Geo002, "Column plumb"),
            (RuleIds.Geo003, "Column continuity"),
            (RuleIds.Geo004, "Pier labels"),
            (RuleIds.Sup001, "Restraints"),
            (RuleIds.Load003, "Line loads"),
            (RuleIds.Load004, "Wall loads")
        };

        public MainForm()
        {
            Text = "ETABS Model Definition Validator";
            Font = Theme.Body;
            BackColor = Theme.Base;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(this.S(1440), this.S(900));
            MinimumSize = new Size(this.S(1000), this.S(640));
            KeyPreview = true;

            // --- Toolbar: connect on the left, profile + run + export on the right ---
            var toolbar = new Panel { Dock = DockStyle.Top, Height = this.S(56), BackColor = Theme.Chrome, Padding = new Padding(this.S(16), 0, this.S(16), 0) };

            var title = new Label
            {
                Text = "Model Definition Validator", AutoSize = false, Dock = DockStyle.Left, Width = this.S(260),
                Font = Theme.DisplayStrong(12.5f), ForeColor = Theme.ChromeText, TextAlign = ContentAlignment.MiddleLeft
            };

            var left = ToolbarFlow(DockStyle.Fill); // fills what title and right leave, so it clips instead of overlapping
            _instanceCombo = Theme.StyleCombo(new ComboBox { Width = this.S(260), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, this.S(2), this.S(6), 0) });
            var refresh = Theme.ChromeButton("Refresh");
            refresh.Click += (s, e) => RefreshInstances();
            _connectButton = Theme.ChromeButton("Connect");
            _connectButton.Click += ConnectButton_Click;
            _connection = new ConnectionBadge();
            left.Controls.AddRange(new Control[] { _instanceCombo, refresh, _connectButton, _connection });

            var right = ToolbarFlow(DockStyle.Right);
            _profileMenu = new ContextMenuStrip { Font = Theme.Body, ShowCheckMargin = true, ShowImageMargin = false };
            foreach (var category in RuleRegistry.AllRules.Select(r => r.Category).Distinct().OrderBy(c => c))
            {
                var item = new ToolStripMenuItem(category) { Checked = true, CheckOnClick = true };
                item.CheckedChanged += (s, e) => UpdateProfileButton();
                _categoryItems.Add(item);
                _profileMenu.Items.Add(item);
            }
            _profileMenu.Items.Add(new ToolStripSeparator());
            _profileMenu.Items.Add("Check all categories", null, (s, e) => _categoryItems.ForEach(i => i.Checked = true));
            _profileMenu.Items.Add(new ToolStripSeparator());
            _configLabel = new ToolStripMenuItem("Thresholds: default profile") { Enabled = false };
            _profileMenu.Items.Add(_configLabel);
            _profileMenu.Items.Add("Load thresholds from file...", null, (s, e) => BrowseConfig());
            _profileMenu.Items.Add("Use default thresholds", null, (s, e) => SetConfigPath(null));
            // Keep the menu open while ticking categories on and off.
            var keepOpen = false;
            _profileMenu.ItemClicked += (s, e) => keepOpen = _categoryItems.Contains(e.ClickedItem);
            _profileMenu.Closing += (s, e) => { if (keepOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; keepOpen = false; };

            _profileButton = Theme.ChromeButton("Profile");
            _profileButton.Click += (s, e) => _profileMenu.Show(_profileButton, new Point(0, _profileButton.Height));

            _runButton = Theme.ChromeButton("Run validation", primary: true);
            _runButton.MinimumSize = new Size(this.S(130), 0);
            _runButton.Enabled = false;
            _runButton.Click += (s, e) => { if (_cts != null) _cts.Cancel(); else RunValidation(); };

            _exportMenu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            _exportCsvItem = new ToolStripMenuItem("Export shown results to CSV...", null, ExportButton_Click) { ShortcutKeyDisplayString = "Ctrl+E", Enabled = false };
            _saveTablesItem = new ToolStripMenuItem("Save tables read from ETABS...", null, SaveTablesButton_Click) { Enabled = false };
            _groupsItem = new ToolStripMenuItem("Group failures of all checks in ETABS...", null, (s, e) => WriteFailureGroups(CheckTabs.Select(t => t.RuleId).ToList())) { Enabled = false };
            _exportMenu.Items.Add(_exportCsvItem);
            _exportMenu.Items.Add(_saveTablesItem);
            _exportMenu.Items.Add(new ToolStripSeparator());
            _exportMenu.Items.Add(_groupsItem);
            _exportButton = Theme.ChromeButton("Export");
            _exportButton.Click += (s, e) => _exportMenu.Show(_exportButton, new Point(0, _exportButton.Height));
            _exportButton.Enabled = false;
            right.Controls.AddRange(new Control[] { _profileButton, _runButton, _exportButton });

            toolbar.Controls.Add(left);
            toolbar.Controls.Add(right);
            toolbar.Controls.Add(title);
            UpdateProfileButton();

            _activity = new ActivityLine();

            // --- Verdict band ---
            _band = new StatusBand();
            _band.SegmentClicked += (category, status) =>
            {
                _tabs.SelectedIndex = ResultsTab;
                _filterToolbar.Show(category, status);
            };

            // --- Results: filters, grid over detail ---
            _filterToolbar = new FilterToolbar();
            _filterToolbar.FilterChanged += (s, filter) =>
            {
                _resultsGrid.ApplyFilter(filter);
                UpdateCounts();
            };

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Line, SplitterWidth = this.S(1) + 4 };
            split.Panel1.BackColor = Theme.Surface;
            split.Panel2.BackColor = Theme.Surface;
            _resultsGrid = new ResultsGridView();
            _resultsGrid.SelectionChanged += (s, result) => _detailPanel.SetResult(result);
            split.Panel1.Controls.Add(_resultsGrid);
            _detailPanel = new ResultDetailPanel();
            split.Panel2.Controls.Add(_detailPanel);
            Load += (s, e) => split.SplitterDistance = (int)(split.Height * 0.55);

            var resultsPage = new Panel { BackColor = Theme.Surface };
            resultsPage.Controls.Add(split);
            resultsPage.Controls.Add(_filterToolbar);

            _checksPanel = new ChecksExplainedPanel();
            _checksPanel.SetData(ConfigurationLoader.LoadDefault(), null);

            _importLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = Theme.Mono, BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None,
                Text = "The import log lists the ETABS tables read, any missing tables and import messages after a run."
            };
            var logPage = new Panel { Padding = new Padding(this.S(20), this.S(14), this.S(8), this.S(8)), BackColor = Theme.Surface };
            logPage.Controls.Add(_importLog);

            _tabs = new TabStrip();
            _tabs.Add("Results", resultsPage);
            _tabs.Add("Checks explained", _checksPanel);
            _tabs.Add("Import log", logPage);
            foreach (var (ruleId, tabTitle) in CheckTabs)
            {
                var page = new CheckTabPage(ruleId);
                page.GroupRequested += (s, e) => WriteFailureGroups(new[] { page.RuleId });
                _checkTabs.Add(page);
                _tabs.Add(tabTitle, page);
            }
            _tabs.Badge = i =>
            {
                if (i == LogTab) return _importProblems > 0 ? _importProblems.ToString("N0") : null;
                var check = i - FirstCheckTab;
                return check >= 0 && check < _checkTabs.Count && _checkTabs[check].FailCount > 0 ? _checkTabs[check].FailCount.ToString("N0") : null;
            };

            _footer = new Footer();

            // Dock order: last added docks first.
            Controls.Add(_tabs.Host);
            Controls.Add(_tabs);
            Controls.Add(_band);
            Controls.Add(_activity);
            Controls.Add(toolbar);
            Controls.Add(_footer);

            RefreshInstances();
        }

        private FlowLayoutPanel ToolbarFlow(DockStyle dock) => new FlowLayoutPanel
        {
            Dock = dock, AutoSize = dock != DockStyle.Fill, WrapContents = false, BackColor = Theme.Chrome,
            Padding = new Padding(0, this.S(13), 0, 0)
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (_cts == null && _runButton.Enabled) RunValidation();
                    return true;
                case Keys.Escape:
                    if (_cts != null) { _cts.Cancel(); return true; }
                    break;
                case Keys.Control | Keys.F:
                    _tabs.SelectedIndex = ResultsTab;
                    _filterToolbar.FocusSearch();
                    return true;
                case Keys.Control | Keys.E:
                    if (_exportCsvItem.Enabled) ExportButton_Click(this, EventArgs.Empty);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // --- Profile -----------------------------------------------------------------------

        private void UpdateProfileButton()
        {
            var on = _categoryItems.Count(i => i.Checked);
            _profileButton.Text = on == _categoryItems.Count ? $"Profile: all {on} categories  ▾"
                : on == 0 ? "Profile: nothing selected  ▾"
                : $"Profile: {on} of {_categoryItems.Count} categories  ▾";
        }

        private void BrowseConfig()
        {
            using (var dialog = new OpenFileDialog { Filter = "Validation profile (*.json)|*.json", Title = "Load thresholds from a validation profile" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) SetConfigPath(dialog.FileName);
            }
        }

        private void SetConfigPath(string path)
        {
            _configPath = path;
            _configLabel.Text = path == null ? "Thresholds: default profile" : "Thresholds: " + System.IO.Path.GetFileName(path);
        }

        // --- ETABS connection --------------------------------------------------------------

        private sealed class EtabsInstance
        {
            public int ProcessId;
            public string Text;
            public override string ToString() => Text;
        }

        private void RefreshInstances()
        {
            _instanceCombo.Items.Clear();
            foreach (var p in EtabsSession.FindRunningEtabs())
            {
                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? "ETABS (no window title)" : p.MainWindowTitle;
                // FindRunningEtabs lists instances with an open model first, so index 0 is the sensible default.
                var notReady = EtabsSession.LooksReady(p) ? "" : "  [no model open?]";
                _instanceCombo.Items.Add(new EtabsInstance { ProcessId = p.Id, Text = $"{title}  (pid {p.Id}){notReady}" });
            }
            if (_instanceCombo.Items.Count > 0) _instanceCombo.SelectedIndex = 0;
            _footer.Status = _instanceCombo.Items.Count == 0
                ? "No running ETABS found. Open your model in ETABS, then press Refresh."
                : $"Found {_instanceCombo.Items.Count} running ETABS. Pick one and press Connect.";
        }

        private async void ConnectButton_Click(object sender, EventArgs e)
        {
            var instance = _instanceCombo.SelectedItem as EtabsInstance;
            if (instance == null)
            {
                MessageBox.Show(this, "No running ETABS is selected. Open your model in ETABS, then press Refresh.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _connectButton.Enabled = false;
            _activity.Running = true;
            _footer.Status = $"Connecting to ETABS (pid {instance.ProcessId})...";
            try
            {
                var session = await System.Threading.Tasks.Task.Run(() => EtabsSession.Attach(instance.ProcessId));
                _connectedPid = session.ProcessId;
                if (_lastRun?.EtabsProcessId != null && _lastRun.EtabsProcessId != session.ProcessId) DisableGrouping();
                _connection.Set(true, session.ModelName);
                _runButton.Enabled = true;
                _footer.Status = $"Connected to {session.ModelName} ({System.IO.Path.GetFileName(session.EtabsFolder)}). Press Run validation or F5.";
            }
            catch (Exception ex)
            {
                _connectedPid = null;
                _connection.Set(false, "Not connected");
                _runButton.Enabled = false;
                _footer.Status = "Could not connect to ETABS.";
                MessageBox.Show(this, ex.Message, "Could not connect to ETABS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _connectButton.Enabled = true;
                _activity.Running = false;
            }
        }

        // --- Run ---------------------------------------------------------------------------

        private async void RunValidation()
        {
            if (!Licensing.LicenseInfo.EnsureLicensed()) return;

            if (_connectedPid == null)
            {
                MessageBox.Show(this, "Connect to a running ETABS model first.", "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var enabledCategories = _categoryItems.Where(i => i.Checked).Select(i => i.Text).ToList();
            if (enabledCategories.Count == 0)
            {
                MessageBox.Show(this, "The profile has no categories selected. Open Profile and tick at least one.", "Nothing to run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ValidationProfileConfig config;
            try
            {
                config = _configPath == null ? ConfigurationLoader.LoadDefault() : ConfigurationLoader.LoadFromFile(_configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The thresholds file could not be read: {ex.Message}", "Thresholds not loaded", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            SetRunning(true);

            var progress = new Progress<string>(msg => _footer.Status = msg);

            try
            {
                ShowRun(await _runner.RunLiveAsync(config, enabledCategories, progress, _cts.Token, _connectedPid), config);
            }
            catch (OperationCanceledException)
            {
                _footer.Status = "Run cancelled. Results from the previous run are still shown.";
            }
            catch (Exception ex)
            {
                _footer.Status = "Run failed.";
                MessageBox.Show(this, ex.Message, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetRunning(false);
            }
        }

        /// <summary>Puts a finished run on screen. Public so a run can be shown without ETABS
        /// (e.g. from an Excel export in a harness).</summary>
        public void ShowRun(ValidationRunResult run, ValidationProfileConfig config)
        {
            _lastRun = run;
            var summary = run.Summary ?? ValidationSummary.From(run.Results, run.Model, null, config?.Reporting);

            _detailPanel.SetConfig(config);
            _checksPanel.SetData(config, run.Results);
            _band.SetSummary(summary);
            _filterToolbar.SetCounts(summary);
            _filterToolbar.SetAvailableCategories(run.Results.Select(r => r.Category));
            _resultsGrid.SetResults(run.Results);
            _resultsGrid.ApplyFilter(_filterToolbar.CurrentFilter);
            _exportButton.Enabled = true;
            _exportCsvItem.Enabled = true;
            _saveTablesItem.Enabled = run.ExtractedTables != null;
            var live = run.EtabsProcessId != null;
            foreach (var tab in _checkTabs) tab.SetRun(run.Results, config, live);
            _groupsItem.Enabled = live && run.Results.Any(r => FailureGroups.For(r.RuleId) != null && r.Status != ValidationStatus.NotChecked);

            _importLog.Text = BuildImportLog(run);
            _importProblems = run.Model.ImportIssues.Count(i => i.Level != ETABSModelDefinitionValidator.Core.Model.ImportIssueLevel.Info);
            _tabs.Invalidate();

            _footer.Status = _importProblems > 0
                ? $"Run finished with {_importProblems:N0} import problem(s). See the Import log tab."
                : $"Run finished. {summary.TotalCount:N0} results from {run.Results.Select(r => r.RuleId).Distinct().Count()} checks.";
            UpdateCounts();
            if (_importProblems > 0 && (run.Results.Count == 0 || (run.ExtractedTables?.Count ?? 0) == 0)) _tabs.SelectedIndex = LogTab;
        }

        private static string BuildImportLog(ValidationRunResult run)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Source: {run.Model.WorkbookName}");

            var a = run.Summary?.Assessment;
            if (a != null)
            {
                sb.AppendLine();
                sb.AppendLine($"RUN VERDICT: {RunAssessment.Label(a.Verdict)}" +
                              (a.CategoriesTotal > 0 ? $"   ({a.CategoriesRun} of {a.CategoriesTotal} categories enabled)" : ""));
                foreach (var r in a.FailReasons) sb.AppendLine("  [Fail] " + r);
                foreach (var r in a.IncompleteReasons) sb.AppendLine("  [Incomplete] " + r);
                foreach (var r in a.ReviewReasons) sb.AppendLine("  [Review] " + r);
            }
            if (run.ConfigIssues != null && run.ConfigIssues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Profile warnings");
                foreach (var i in run.ConfigIssues) sb.AppendLine("  " + i);
            }

            sb.AppendLine();
            sb.AppendLine("Import state per table (Read / ReadEmpty / Partial / SchemaInvalid / FailedToRead / Ambiguous / Missing / Skipped / Unrecognized)");
            foreach (var t in run.Model.TableReports.Values.OrderBy(t => t.State == ETABSModelDefinitionValidator.Core.Model.TableImportState.Read ? 1 : 0).ThenBy(t => t.Title))
            {
                sb.AppendLine("  " + t);
                foreach (var m in t.Messages.Take(5)) sb.AppendLine("      " + m);
                if (t.Messages.Count > 5) sb.AppendLine($"      ... {t.Messages.Count - 5} more");
            }

            sb.AppendLine();
            sb.AppendLine("Tables read from ETABS");
            if (run.ExtractedTables == null || run.ExtractedTables.Count == 0) sb.AppendLine("  (none)");
            else foreach (var t in run.ExtractedTables)
            {
                sb.AppendLine($"  {t.TableTitle}  -  {t.Rows.Count} record(s)");
                sb.AppendLine($"      columns: {string.Join(" | ", t.Columns.Select(c => c.OriginalHeader))}");
            }

            sb.AppendLine();
            sb.AppendLine("Missing tables (their checks show Not checked)");
            if (run.Model.MissingTables.Count == 0) sb.AppendLine("  (none)");
            else foreach (var m in run.Model.MissingTables) sb.AppendLine("  " + m);

            sb.AppendLine();
            sb.AppendLine("Messages");
            if (run.Model.ImportIssues.Count == 0) sb.AppendLine("  (none)");
            else foreach (var i in run.Model.ImportIssues) sb.AppendLine($"  [{i.Level}] {i.Message}");

            if (run.EtabsTables != null && run.EtabsTables.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"All tables ETABS offers for this model ({run.EtabsTables.Count})");
                foreach (var t in run.EtabsTables.OrderBy(t => t.Name)) sb.AppendLine("  " + t.Name);
            }

            return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        private void SetRunning(bool running)
        {
            _activity.Running = running;
            _runButton.Text = running ? "Cancel (Esc)" : "Run validation";
            _runButton.Enabled = running || _connectedPid != null;
            _connectButton.Enabled = !running;
            _profileButton.Enabled = !running;
            foreach (var tab in _checkTabs) tab.SetBusy(running);
        }

        // --- ETABS failure groups ------------------------------------------------------------

        /// <summary>The shown results belong to another ETABS instance than the one now connected:
        /// grouping stays off until the next run, so names from one model never land in another.</summary>
        private void DisableGrouping()
        {
            _groupsItem.Enabled = false;
            foreach (var tab in _checkTabs) tab.DisableGrouping();
            _footer.Status = "Connected to a different ETABS than the shown results came from - run again to group failures.";
        }

        /// <summary>Writes the failing objects of the given checks into their VChk_* groups in the
        /// connected model, after the user confirms - this is the only action that changes the model.</summary>
        private async void WriteFailureGroups(IReadOnlyList<string> ruleIds)
        {
            if (_lastRun?.EtabsProcessId == null || _cts != null) return;

            // Only checks that actually ran: a NOT_CHECKED check must not empty a group it never evaluated.
            var ran = ruleIds.Where(id => _lastRun.Results.Any(r => r.RuleId == id && r.Status != ValidationStatus.NotChecked)).ToList();
            var groups = ValidationRunner.BuildFailureGroups(_lastRun.Results, ran);
            if (groups.Count == 0) return;

            var lines = groups.Select(g => $"  {g.Key}: {g.Value.Count:N0} object(s)" + (g.Value.Count == 0 ? " (emptied if it exists)" : ""));
            var answer = MessageBox.Show(this,
                $"This changes the ETABS model {System.IO.Path.GetFileName(_lastRun.EtabsModelPath)} (pid {_lastRun.EtabsProcessId}) - the model these results were read from: each group below is emptied and then filled with the objects that failed its check, shown in red.\n\n" +
                string.Join("\n", lines) + "\n\nNothing else in the model is touched. Continue?",
                "Group failures in ETABS", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _groupsItem.Enabled = false;
            foreach (var tab in _checkTabs) tab.SetBusy(true);
            _activity.Running = true;
            _footer.Status = "Writing failure groups to ETABS...";
            try
            {
                var written = await _runner.WriteFailureGroupsAsync(_lastRun, ran);
                var text = written.Count == 0 ? "No groups needed writing." : string.Join("\n", written.Select(w => w.ToString()));
                _footer.Notify("Failure groups written to ETABS. Select them with Select > Select > Groups.");
                MessageBox.Show(this, text + "\n\nIn ETABS: Select > Select > Groups.", "Groups written", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _footer.Status = "Writing groups failed.";
                MessageBox.Show(this, ex.Message, "Groups not written", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _activity.Running = false;
                _groupsItem.Enabled = _lastRun?.EtabsProcessId == _connectedPid;
                foreach (var tab in _checkTabs) tab.SetBusy(false);
            }
        }

        private void UpdateCounts()
        {
            _footer.Right = $"Showing {_resultsGrid.VisibleCount:N0} of {_resultsGrid.TotalCount:N0} results";
        }

        // --- Export ------------------------------------------------------------------------

        private void SaveTablesButton_Click(object sender, EventArgs e)
        {
            if (_lastRun?.ExtractedTables == null) return;
            using (var dialog = new FolderBrowserDialog { Description = "Folder for the tables read from ETABS (one CSV per table)" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var files = ExtractedTableWriter.WriteAll(dialog.SelectedPath, _lastRun.ExtractedTables, _lastRun.EtabsTables);
                    _footer.Notify($"Saved {files.Count} table file(s) to {dialog.SelectedPath}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"The tables could not be saved: {ex.Message}", "Tables not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_lastRun == null) return;

            using (var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "validation-report.csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    CsvReportWriter.WriteToFile(_resultsGrid.CurrentView, dialog.FileName);
                    _footer.Notify($"Exported {_resultsGrid.CurrentView.Count:N0} results to {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"The CSV could not be written: {ex.Message}", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
