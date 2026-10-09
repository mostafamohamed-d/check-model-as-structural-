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
        public static RichTextBox NewBox() => new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            DetectUrls = false
        };

        public static void Title(RichTextBox box, string text, Color color)
        {
            Append(box, text + "\n", new Font("Segoe UI", 13f, FontStyle.Bold), color);
        }

        public static void Section(RichTextBox box, string heading, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            Append(box, "\n" + heading + "\n", new Font("Segoe UI", 9.5f, FontStyle.Bold), Color.FromArgb(40, 70, 120));
            Append(box, body + "\n", box.Font, Color.Black);
        }

        public static void Explanation(RichTextBox box, RuleExplanation e)
        {
            Section(box, "What this check does", e.WhatItChecks);
            Section(box, "Why it matters (engineering)", e.WhyItMatters);
            Section(box, "Passes when", e.PassCondition);
            Section(box, "If it does not pass", e.HowToFix);
            Section(box, "Basis - " + TypeLabel(e.Type), e.Basis);
            Section(box, "Engineer's note / limits of this check", e.EngineerNote);
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
