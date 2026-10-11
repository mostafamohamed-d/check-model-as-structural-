using System.Drawing;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Renders headed paragraphs into a RichTextBox, so the result detail view and the
    /// Checks Explained tab read the same way.</summary>
    internal static class ExplanationWriter
    {
        private static readonly Font HeadingFont = Theme.DisplayStrong(10.5f);
        private static readonly Font TitleFont = Theme.DisplayStrong(15f);

        public static RichTextBox NewBox() => new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            Font = new Font("Segoe UI", 9.5f),
            DetectUrls = false
        };

        public static void Title(RichTextBox box, string text, Color color)
        {
            Append(box, text + "\n", TitleFont, color);
        }

        public static void Section(RichTextBox box, string heading, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            Append(box, (box.TextLength == 0 ? "" : "\n") + heading + "\n", HeadingFont, Theme.AccentDark);
            Append(box, body + "\n", box.Font, Theme.Ink);
        }

        public static void Explanation(RichTextBox box, RuleExplanation e)
        {
            Section(box, "Authority", e.Authority);
            Section(box, "What this check does", e.WhatItChecks);
            Section(box, "Applies to", e.Applicability);
            Section(box, "Why it matters", e.WhyItMatters);
            Section(box, "Passes when", e.PassCondition);
            Section(box, "Tolerance", e.Tolerance);
            Section(box, "Reference", e.Reference);
            Section(box, "Severity", e.SeverityBasis);
            Section(box, "Exceptions", e.Exceptions);
            Section(box, "Acceptable alternatives", e.AcceptableAlternatives);
            Section(box, "How to fix it", e.HowToFix);
            Section(box, "Basis: " + TypeLabel(e.Type), e.Basis);
            Section(box, "Engineer's note and limits of this check", e.EngineerNote);
            Section(box, "ETABS table read", e.Table);
        }

        public static string TypeLabel(RuleType t)
        {
            switch (t)
            {
                case RuleType.Code: return "code requirement";
                case RuleType.ProjectStandard: return "office / project standard";
                case RuleType.ModelingStandard: return "modelling standard";
                case RuleType.Consistency: return "consistency check";
                case RuleType.ModelIntegrity: return "model integrity";
                default: return "best practice";
            }
        }

        private static void Append(RichTextBox box, string text, Font font, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = font;
            box.SelectionColor = color;
            box.AppendText(text);
        }
    }
}
