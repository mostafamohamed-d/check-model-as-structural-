using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>One place for the app's colours, fonts and the small drawing helpers the custom
    /// controls share. Status colours follow the ETABS demand/capacity reading: green is fine,
    /// amber needs a look, red must be fixed.</summary>
    internal static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x33);
        public static readonly Color InkMuted = Color.FromArgb(0x5B, 0x68, 0x73);
        public static readonly Color Base = Color.FromArgb(0xEC, 0xEE, 0xEF);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(0xF6, 0xF7, 0xF8);
        public static readonly Color Line = Color.FromArgb(0xD5, 0xD9, 0xDC);
        public static readonly Color Accent = Color.FromArgb(0x2F, 0x6F, 0x9F);
        public static readonly Color AccentDark = Color.FromArgb(0x24, 0x5A, 0x82);
        public static readonly Color Selection = Color.FromArgb(0xDC, 0xE8, 0xF2);

        // Toolbar chrome: slate, like the ETABS model window frame.
        public static readonly Color Chrome = Color.FromArgb(0x22, 0x31, 0x3C);
        public static readonly Color ChromeRaised = Color.FromArgb(0x2E, 0x40, 0x4D);
        public static readonly Color ChromeLine = Color.FromArgb(0x46, 0x5A, 0x68);
        public static readonly Color ChromeText = Color.FromArgb(0xE6, 0xEC, 0xEF);
        public static readonly Color ChromeTextMuted = Color.FromArgb(0x9F, 0xAF, 0xBA);

        public static readonly Color Pass = Color.FromArgb(0x2E, 0x7D, 0x4F);
        public static readonly Color Warning = Color.FromArgb(0xC9, 0x8A, 0x00);
        public static readonly Color Fail = Color.FromArgb(0xB8, 0x32, 0x27);
        public static readonly Color Exempt = Color.FromArgb(0x7A, 0x7F, 0x85);
        public static readonly Color NotChecked = Color.FromArgb(0x6B, 0x6F, 0xA8);

        /// <summary>Worst first: the order bars and chips are drawn in.</summary>
        public static readonly ValidationStatus[] StatusOrder =
        {
            ValidationStatus.Fail, ValidationStatus.Warning, ValidationStatus.NotChecked, ValidationStatus.Exempt, ValidationStatus.Pass
        };

        public static Color ColorForStatus(ValidationStatus status)
        {
            switch (status)
            {
                case ValidationStatus.Pass: return Pass;
                case ValidationStatus.Warning: return Warning;
                case ValidationStatus.Fail: return Fail;
                case ValidationStatus.Exempt: return Exempt;
                default: return NotChecked;
            }
        }

        public static string StatusText(ValidationStatus status) =>
            status == ValidationStatus.NotChecked ? "Not checked" : status.ToString();

        /// <summary>Pale fill behind status text, so a pill reads at a glance without shouting.</summary>
        public static Color Tint(Color c, float amount = 0.86f) => Color.FromArgb(
            (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

        // --- Fonts --------------------------------------------------------------------------

        private static readonly string[] Installed = new InstalledFontCollection().Families.Select(f => f.Name).ToArray();
        private static string Pick(params string[] names) => names.FirstOrDefault(n => Installed.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? "Segoe UI";

        /// <summary>Bahnschrift (DIN-style, ships with Windows 10+) for headings and counts.</summary>
        public static readonly string DisplayFamily = Pick("Bahnschrift");
        public static readonly string DisplaySemiBoldFamily = Pick("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold");
        public static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas");

        public static readonly Font Body = new Font("Segoe UI", 9f);
        public static readonly Font BodyStrong = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font SmallStrong = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font Mono = new Font(MonoFamily, 9f);

        public static Font Display(float size) => new Font(DisplayFamily, size);
        public static Font DisplayStrong(float size) => new Font(DisplaySemiBoldFamily, size);

        // --- Layout / drawing helpers -------------------------------------------------------

        /// <summary>Scale a 96-dpi pixel size to the control's actual DPI.</summary>
        public static int S(this Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96f);

        public static void EnableDoubleBuffer(Control c) =>
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(c, true, null);

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>A rounded status pill centred vertically in <paramref name="cell"/>.</summary>
        public static void DrawStatusPill(Graphics g, Rectangle cell, ValidationStatus status, Control scaleFrom)
        {
            var text = StatusText(status);
            var color = ColorForStatus(status);
            var size = TextRenderer.MeasureText(g, text, SmallStrong, Size.Empty, TextFormatFlags.NoPadding);
            var h = scaleFrom.S(20);
            var pill = new Rectangle(cell.X + scaleFrom.S(8), cell.Y + (cell.Height - h) / 2, size.Width + scaleFrom.S(28), h);
            // Grids share one Graphics across all cells: leaving AntiAlias on would soften every
            // later cell fill and leave a 1px seam between columns, so restore it.
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(pill, h / 2))
            using (var fill = new SolidBrush(Tint(color)))
                g.FillPath(fill, path);
            var dot = scaleFrom.S(6);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, pill.X + scaleFrom.S(9), pill.Y + (h - dot) / 2, dot, dot);
            g.SmoothingMode = smoothing;
            var textRect = new Rectangle(pill.X + scaleFrom.S(19), pill.Y, pill.Width - scaleFrom.S(19), h);
            TextRenderer.DrawText(g, text, SmallStrong, textRect, Shade(color), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        /// <summary>Darker variant for text on a tint, so amber stays readable.</summary>
        public static Color Shade(Color c, float amount = 0.22f) => Color.FromArgb(
            (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

        public static void StyleGrid(DataGridView grid)
        {
            EnableDoubleBuffer(grid);
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Surface;
            grid.GridColor = Color.FromArgb(0xE6, 0xE9, 0xEB);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.RowHeadersVisible = false;
            grid.AllowUserToResizeRows = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = grid.S(34);
            grid.RowTemplate.Height = grid.S(30);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceAlt, ForeColor = InkMuted, Font = SmallStrong,
                SelectionBackColor = SurfaceAlt, SelectionForeColor = InkMuted,
                Padding = new Padding(grid.S(6), 0, 0, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface, ForeColor = Ink, Font = Body,
                SelectionBackColor = Selection, SelectionForeColor = Ink,
                Padding = new Padding(grid.S(6), 0, grid.S(4), 0)
            };
        }

        /// <summary>Flat button. Primary is the one action that moves the job forward (Run).</summary>
        public static Button ChromeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                Font = primary ? BodyStrong : Body, UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : ChromeRaised, ForeColor = primary ? Color.White : ChromeText,
                Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 6, 0)
            };
            b.FlatAppearance.BorderColor = primary ? Accent : ChromeLine;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0x3A, 0x80, 0xB4) : Color.FromArgb(0x3A, 0x4E, 0x5C);
            b.FlatAppearance.MouseDownBackColor = primary ? AccentDark : Chrome;
            b.EnabledChanged += (s, e) =>
            {
                b.ForeColor = b.Enabled ? (primary ? Color.White : ChromeText) : ChromeTextMuted;
                if (primary) b.BackColor = b.FlatAppearance.BorderColor = b.Enabled ? Accent : ChromeRaised;
            };
            return b;
        }

        public static ComboBox StyleCombo(ComboBox c)
        {
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Body;
            return c;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Grey hint text shown in an empty TextBox.</summary>
        public static void SetCue(TextBox box, string cue)
        {
            void Apply() => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, cue);
            if (box.IsHandleCreated) Apply(); else box.HandleCreated += (s, e) => Apply();
        }
    }
}
