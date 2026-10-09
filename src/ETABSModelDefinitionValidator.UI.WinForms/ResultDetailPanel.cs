using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Detail view of one selected result: the finding (expected / actual / source row)
    /// followed by the plain-language explanation of the check that produced it.</summary>
    public sealed class ResultDetailPanel : UserControl
    {
        private readonly RichTextBox _box;
        private ValidationProfileConfig _config = new ValidationProfileConfig();

        public ResultDetailPanel()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(8);
            _box = ExplanationWriter.NewBox();
            Controls.Add(_box);
        }

        public void SetConfig(ValidationProfileConfig config) => _config = config ?? new ValidationProfileConfig();

        public void SetResult(ValidationResult r)
        {
            _box.Clear();
            if (r == null) return;

            ExplanationWriter.Title(_box, $"{r.Status.ToString().ToUpperInvariant()}  -  {r.RuleId}  {r.RuleName}", SummaryPanel.ColorForStatus(r.Status));

            ExplanationWriter.Section(_box, "Finding",
                $"{r.ObjectType} '{r.ObjectName}'" + (string.IsNullOrEmpty(r.Story) ? "" : $"  (story {r.Story})") + "\n" +
                (string.IsNullOrEmpty(r.FieldName) ? "" : $"Field:     {r.FieldName}\n") +
                (r.ExpectedValue != null ? $"Expected:  {r.ExpectedValue}\n" : "") +
                (r.ActualValue != null ? $"Actual:    {r.ActualValue}\n" : "") +
                $"\n{r.Message}");

            ExplanationWriter.Section(_box, "Technical explanation", r.TechnicalExplanation);
            if (r.CodeBasis != null) ExplanationWriter.Section(_box, "Code reference", r.CodeBasis.ToString());

            var e = RuleCatalog.Find(r.RuleId, _config);
            if (e != null)
            {
                _box.AppendText("\n------------------------------------------------------------\n");
                ExplanationWriter.Explanation(_box, e);
            }

            ExplanationWriter.Section(_box, "Where to find it",
                $"Workbook:  {r.Source?.WorkbookName}\nWorksheet: {r.Source?.WorksheetName}\nTable:     {r.Source?.TableName}\nRow:       {r.Source?.RowNumber}\n" +
                $"Severity:  {r.Severity}");

            _box.SelectionStart = 0;
            _box.ScrollToCaret();
        }
    }
}
