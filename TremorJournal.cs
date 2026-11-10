using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Journal du tremblement : toutes les 10 minutes, résume les mesures du moteur
    /// en une ligne CSV par catégorie (repos / geste) dans
    /// %APPDATA%\MouseSmooth\journal.csv. Aucune trajectoire n'est enregistrée :
    /// seulement des chiffres (donnée de santé, reste sur ce PC).
    /// Format « ; » et virgule décimale : s'ouvre directement dans Excel français.
    /// </summary>
    internal sealed class TremorJournal : IDisposable
    {
        public const double RestBelowPxS = 20;      // main posée : le tremblement seul déplace peu
        public const double GestureBelowPxS = 400;  // au-delà, le geste rapide fausserait la mesure
        public const double MinAmplitudePx = 1.0;   // en dessous : bruit d'arrondi au pixel, pas un tremblement
        private const int MinSegments = 3;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "MouseSmooth", "journal.csv");

        private readonly MotionEngine _engine;
        private readonly SmoothConfig _config;
        private readonly Timer _timer = new Timer { Interval = 10 * 60 * 1000 };

        public TremorJournal(MotionEngine engine, SmoothConfig config)
        {
            _engine = engine;
            _config = config;
            _timer.Tick += (s, e) => Flush();
            _timer.Start();
        }

        /// <summary>Écrit le résumé des mesures accumulées (appelé aussi à la fermeture).</summary>
        public void Flush()
        {
            List<JournalSample> samples = _engine.DrainJournal();
            var lines = new List<string>();
            AddLine(lines, "repos", samples.Where(s => s.NetSpeedPxS < RestBelowPxS).ToList());
            AddLine(lines, "geste", samples.Where(s => s.NetSpeedPxS >= RestBelowPxS && s.NetSpeedPxS < GestureBelowPxS).ToList());
            if (lines.Count == 0) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (!File.Exists(FilePath))
                    lines.Insert(0, "date;heure;situation;segments_3s;frequence_hz;amplitude_px;amplitude_px_max;lissage_ms");
                File.AppendAllLines(FilePath, lines);
            }
            catch
            {
                // Fichier ouvert dans Excel (verrouillé)… : ces mesures sont perdues, l'appli continue.
            }
        }

        private void AddLine(List<string> lines, string situation, List<JournalSample> s)
        {
            if (s.Count < MinSegments) return;
            DateTime now = DateTime.Now;
            lines.Add(string.Join(";",
                now.ToString("yyyy-MM-dd"), now.ToString("HH:mm"), situation, s.Count,
                Median(s.Select(x => x.FrequencyHz)).ToString("0.0"),
                Median(s.Select(x => x.AmplitudePx)).ToString("0.0"),
                s.Max(x => x.AmplitudePx).ToString("0.0"),
                _config.SmoothingMs.ToString("0")));
        }

        public static double Median(IEnumerable<double> values)
        {
            var v = values.OrderBy(x => x).ToArray();
            return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
        }

        public void Dispose()
        {
            _timer.Dispose();
            Flush();
        }
    }
}
