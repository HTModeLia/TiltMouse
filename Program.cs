using System;
using System.Windows.Forms;

namespace MouseSmooth
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Auto-test console : vérifie le filtre sans toucher au curseur réel.
            if (Array.IndexOf(args, "--selftest") >= 0)
            {
                return SelfTest.Run();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext());
            return 0;
        }
    }
}
