using System;
using System.IO;
using System.Text.Json;

namespace MouseSmooth
{
    /// <summary>Paramètres du lisseur, persistés côté utilisateur.</summary>
    internal sealed class SmoothConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Mode utilisateur valide : le hook RESTE installé mais aucun traitement
        /// n'est appliqué (pass-through). Le profil tremblant est conservé intact
        /// et revient dès la sortie du mode.
        /// </summary>
        public bool ValidMode { get; set; } = false;

        /// <summary>Sensibilité axe X (1.0 = vitesse native).</summary>
        public double GainX { get; set; } = 1.0;

        /// <summary>Sensibilité axe Y (1.0 = vitesse native).</summary>
        public double GainY { get; set; } = 1.0;

        /// <summary>Force du lissage : 0 = natif, 0.95 = maximal.</summary>
        public double Smoothing { get; set; } = 0.6;

        /// <summary>
        /// Seuil de tremblement en pixels : en dessous de cette vitesse d'écriture
        /// (px/s sur le suivi lent), le mouvement est assimilé à du tremblement et le
        /// curseur reste immobile ; au-dessus, le lissage s'applique normalement.
        /// </summary>
        public double TremorThreshold { get; set; } = 30.0;

        /// <summary>Bande morte X en pixels (micro-déplacements ignorés).</summary>
        public double DeadbandX { get; set; } = 2.0;

        /// <summary>Bande morte Y en pixels.</summary>
        public double DeadbandY { get; set; } = 2.0;

        public void Clamp()
        {
            GainX = Math.Min(3.0, Math.Max(0.1, GainX));
            GainY = Math.Min(3.0, Math.Max(0.1, GainY));
            Smoothing = Math.Min(0.95, Math.Max(0.0, Smoothing));
            TremorThreshold = Math.Min(200.0, Math.Max(0.0, TremorThreshold));
            DeadbandX = Math.Min(20.0, Math.Max(0.0, DeadbandX));
            DeadbandY = Math.Min(20.0, Math.Max(0.0, DeadbandY));
        }

        public void ResetToDefaults()
        {
            var d = new SmoothConfig();
            Enabled = d.Enabled;
            ValidMode = d.ValidMode;
            GainX = d.GainX;
            GainY = d.GainY;
            Smoothing = d.Smoothing;
            TremorThreshold = d.TremorThreshold;
            DeadbandX = d.DeadbandX;
            DeadbandY = d.DeadbandY;
        }

        public SmoothConfig Clone() => (SmoothConfig)MemberwiseClone();
    }

    internal static class SettingsStore
    {
        private static string DirectoryPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseSmooth");

        private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

        public static SmoothConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<SmoothConfig>(File.ReadAllText(FilePath));
                    if (cfg != null)
                    {
                        cfg.Clamp();
                        return cfg;
                    }
                }
            }
            catch
            {
                // Fichier absent/corrompu : valeurs par défaut.
            }
            return new SmoothConfig();
        }

        public static void Save(SmoothConfig config)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(config, options));
            }
            catch
            {
                // La persistance ne doit jamais faire planter l'application.
            }
        }
    }
}
