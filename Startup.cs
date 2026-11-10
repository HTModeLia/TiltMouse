using System;
using Microsoft.Win32;

namespace MouseSmooth
{
    /// <summary>Lancement à l'ouverture de session (clé Run de l'utilisateur : aucun droit administrateur).</summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MouseSmooth";

        private static string Command => $"\"{Environment.ProcessPath}\"";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                        return key?.GetValue(ValueName) != null;
                }
                catch { return false; }
            }
        }

        /// <summary>Active ou retire le lancement automatique. Renvoie l'état réellement obtenu.</summary>
        public static bool Set(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) key.SetValue(ValueName, Command);
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch
            {
                // Registre verrouillé (stratégie de groupe…) : l'état affiché reflète la réalité.
            }
            return IsEnabled;
        }

        /// <summary>L'exécutable a été déplacé : la clé suit, sinon le démarrage automatique serait cassé.</summary>
        public static void RefreshPath()
        {
            if (IsEnabled) Set(true);
        }
    }
}
