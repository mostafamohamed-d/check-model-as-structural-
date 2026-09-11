using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Read-only detail view of one selected ValidationResult - every field an engineer
    /// needs to find and understand the issue, including full source traceability.</summary>
    public sealed class ResultDetailPanel : UserControl
    {
        private readonly TextBox _text;

        public ResultDetailPanel()
        {
            Dock = DockStyle.Fill;
            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9),
                BackColor = System.Drawing.SystemColors.Window
            };
            Controls.Add(_text);
        }

        public void SetResult(ValidationResult r)
        {
            if (r == null)
            {
                _text.Text = string.Empty;
                return;
            }

            _text.Text =
                $"{r.Status}\r\n\r\n" +
                $"Rule ID:     {r.RuleId}\r\n" +
                $"Rule Name:   {r.RuleName}\r\n" +
                $"Category:    {r.Category}\r\n" +
                $"Rule Type:   {r.RuleType}\r\n" +
                $"Severity:    {r.Severity}\r\n\r\n" +
                $"Object:      {r.ObjectType} '{r.ObjectName}'\r\n" +
                (string.IsNullOrEmpty(r.Story) ? "" : $"Story:       {r.Story}\r\n") +
                (string.IsNullOrEmpty(r.FieldName) ? "" : $"Field:       {r.FieldName}\r\n") + "\r\n" +
                (r.ExpectedValue != null ? $"Expected:    {r.ExpectedValue}\r\n" : "") +
                (r.ActualValue != null ? $"Actual:      {r.ActualValue}\r\n" : "") + "\r\n" +
                $"Message:\r\n{r.Message}\r\n\r\n" +
                (string.IsNullOrEmpty(r.TechnicalExplanation) ? "" : $"Technical explanation:\r\n{r.TechnicalExplanation}\r\n\r\n") +
                (r.CodeBasis != null ? $"Code basis:  {r.CodeBasis}\r\n\r\n" : "") +
                $"Source:\r\n  Workbook:  {r.Source?.WorkbookName}\r\n" +
                $"  Worksheet: {r.Source?.WorksheetName}\r\n" +
                $"  Table:     {r.Source?.TableName}\r\n" +
                $"  Row:       {r.Source?.RowNumber}\r\n";
        }
    }
}
