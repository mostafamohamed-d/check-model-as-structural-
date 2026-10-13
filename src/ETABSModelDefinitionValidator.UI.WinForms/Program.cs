using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            // Without this Windows bitmap-stretches the window on 125%+ displays (blurry text);
            // the custom controls scale their own pixel sizes from DeviceDpi.
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!Licensing.LicenseInfo.EnsureLicensed()) return;
            Application.Run(new MainForm());
        }
    }
}
