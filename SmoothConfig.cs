using System;
using System.IO;
using System.Text.Json;

namespace MouseSmooth
{
    /// <summary>Paramètres du lisseur, persistés côté utilisateur.</summary>
    internal sealed class SmoothConfig
    {
        /// <summary>Lissage actif (sinon la souris est native, le filtre tourne en simulation).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Durée de la moyenne glissante (ms). Idéalement égale à la période du
        /// tremblement (200 ms pour 5 Hz) : le tremblement est alors annulé. Le
        /// curseur suit la main avec un retard d'environ cette durée. 0 = natif.
        /// </summary>
        public double SmoothingMs { get; set; } = 200.0;

        /// <summary>
        /// Réactivité aux gestes rapides (0..1) : pendant un grand déplacement, la
        /// moyenne est raccourcie pour réduire le retard. 0 = moyenne toujours complète.
        /// </summary>
        public double Reactivity { get; set; } = 0.5;

        /// <summary>Vitesse du curseur (1.0 = vitesse Windows).</summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Stabilisation des clics (ms) : les mouvements de la main pendant cette
        /// durée après un appui ou un relâchement de bouton sont ignorés (le geste
        /// d'appuyer fait souvent bouger la souris). 0 = désactivé.
        /// </summary>
        public double ClickStabilizeMs { get; set; } = 120.0;

        public void Clamp()
        {
            SmoothingMs = Math.Min(500.0, Math.Max(0.0, SmoothingMs));
            Reactivity = Math.Min(1.0, Math.Max(0.0, Reactivity));
            Speed = Math.Min(3.0, Math.Max(0.25, Speed));
            ClickStabilizeMs = Math.Min(300.0, Math.Max(0.0, ClickStabilizeMs));
        }

        public void ResetToDefaults() => CopyFrom(new SmoothConfig());

        public void CopyFrom(SmoothConfig source)
        {
            Enabled = source.Enabled;
            SmoothingMs = source.SmoothingMs;
            Reactivity = source.Reactivity;
            Speed = source.Speed;
            ClickStabilizeMs = source.ClickStabilizeMs;
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
                    // Les champs d'anciennes versions (seuil, bande morte…) sont ignorés.
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
