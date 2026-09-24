using System;
using System.IO;
using System.Windows.Forms;

namespace MouseSmooth
{
    internal static class Program
    {
        /// <summary>Chemin du journal d'erreurs (%APPDATA%\MouseSmooth\error.log).</summary>
        private static string LogPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "MouseSmooth", "error.log");

        [STAThread]
        private static int Main(string[] args)
        {
            // Auto-test console : vérifie le filtre sans toucher au curseur réel.
            if (Array.IndexOf(args, "--selftest") >= 0)
            {
                return SelfTest.Run();
            }

            // Filets de sécurité : toute exception non gérée est journalisée (et
            // l'application se ferme proprement) au lieu de planter silencieusement.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log("AppDomain", e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log("UIThread", e.Exception);

            ReportPreviousCrash();
            Application.Run(new TrayContext());
            return 0;
        }

        /// <summary>Journalise une exception avec date, type, message et pile.</summary>
        private static void Log(string source, Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().Name}: {ex.Message}\n" +
                    $"{ex.StackTrace}\n\n");
            }
            catch
            {
                // On ne peut rien de plus : ne jamais faire planter le journalisateur.
            }
        }

        /// <summary>Si le démarrage précédent s'est terminé sur une exception, la signaler.</summary>
        private static void ReportPreviousCrash()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    MessageBox.Show(
                        "Le démarrage précédent s'est terminé sur une erreur :\n\n" +
                        File.ReadAllText(LogPath) +
                        "\nLe journal est vidé maintenant ; ce message ne réapparaîtra que si une nouvelle erreur survient.",
                        "MouseSmooth — rapport d'erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    File.Delete(LogPath);
                }
            }
            catch
            {
                // L'absence de journal ne doit jamais empêcher le démarrage.
            }
        }
    }
}
