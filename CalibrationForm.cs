using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Assistant de calibrage : mesure le tremblement réel de la main (main posée,
    /// sans intention de mouvement) puis propose un seuil, un lissage et une bande
    /// morte adaptés. La mesure porte sur les ÉVÉNEMENTS BRUTS du hook (avant tout
    /// lissage) : elle reflète le matériel, pas le comportement filtré.
    /// </summary>
    internal sealed class CalibrationForm : Form
    {
        private enum Phase { Idle, Countdown, Measuring, Done }

        private readonly MouseHookEngine _engine;
        private Phase _phase = Phase.Idle;
        private Timer _timer;
        private int _ticksLeft;
        private readonly SmoothConfig _recommended = new SmoothConfig();

        private readonly Label _lblMain;
        private readonly Label _lblSub;
        private readonly ProgressBar _bar;
        private readonly Button _btnStart, _btnApply, _btnCancel;

        /// <summary>Déclenché quand l'utilisateur applique les réglages recommandés.</summary>
        public event Action<SmoothConfig> Applied;

        public CalibrationForm(MouseHookEngine engine, SmoothConfig current)
        {
            _engine = engine;
            _recommended.CopyFrom(current);

            Text = "MouseSmooth — Calibrage";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 210);

            _lblMain = new Label
            {
                Text = "Posez la main sur la souris, sans intention de mouvement.\n" +
                       "Le tremblement réel va être mesuré pendant 5 secondes.",
                Location = new Point(16, 15),
                Size = new Size(408, 44)
            };

            _lblSub = new Label
            {
                Text = "",
                ForeColor = Color.DimGray,
                Location = new Point(16, 64),
                Size = new Size(408, 22)
            };

            _bar = new ProgressBar
            {
                Location = new Point(16, 92),
                Size = new Size(408, 23),
                Minimum = 0,
                Maximum = 100
            };

            _btnStart = new Button { Text = "Démarrer la mesure", Width = 140, Location = new Point(16, 130) };
            _btnApply = new Button { Text = "Appliquer les réglages recommandés", Width = 220, Location = new Point(16, 130), Enabled = false };
            _btnCancel = new Button { Text = "Fermer", Width = 90, Location = new Point(334, 168), DialogResult = DialogResult.Cancel };

            Controls.Add(_lblMain);
            Controls.Add(_lblSub);
            Controls.Add(_bar);
            Controls.Add(_btnStart);
            Controls.Add(_btnApply);
            Controls.Add(_btnCancel);
            CancelButton = _btnCancel;

            _btnStart.Click += OnStart;
            _btnApply.Click += (s, e) => { Applied?.Invoke(_recommended); Close(); };
        }

        /// <summary>Lance le compte à rebours (3 s) puis la mesure (5 s).</summary>
        private void OnStart(object sender, EventArgs e)
        {
            if (_timer == null)
            {
                _timer = new Timer { Interval = 100 };
                _timer.Tick += OnTick;
            }

            _phase = Phase.Countdown;
            _ticksLeft = 30; // 3 s à 100 ms
            _bar.Value = 0;
            _lblSub.Text = "Préparez-vous…";
            _btnStart.Enabled = false;
            _btnApply.Enabled = false;
            _timer.Start();
        }

        /// <summary>Machine à états du calibrage, pilotée à 10 Hz.</summary>
        private void OnTick(object sender, EventArgs e)
        {
            _ticksLeft--;

            if (_phase == Phase.Countdown)
            {
                if (_ticksLeft > 0)
                {
                    _lblSub.Text = "Ne bougez plus… " + Math.Ceiling(_ticksLeft / 10.0) + " s";
                    return;
                }

                _phase = Phase.Measuring;
                _ticksLeft = 50; // 5 s à 100 ms
                _bar.Value = 0;
                _engine.StartCalibration();
                _lblSub.Text = "Mesure en cours — gardez la main détendue…";
                return;
            }

            if (_phase == Phase.Measuring)
            {
                _bar.Value = Math.Min(100, (50 - _ticksLeft) * 2);
                if (_ticksLeft > 0) return;

                _engine.StopCalibration();
                _timer.Stop();
                ComputeRecommendation();

                _phase = Phase.Done;
                _lblMain.Text = "Calibrage terminé.";
                _lblSub.Text = "Appliquez les réglages recommandés, ou recommencez la mesure.";
                _btnApply.Enabled = true;
                _btnStart.Text = "Recommencer";
                _btnStart.Enabled = true;
            }
        }

        /// <summary>
        /// Analyse des échantillons bruts : écart maximal à la position moyenne du
        /// tremblement => recommandations (seuil au-dessus du tremblement mesuré).
        /// </summary>
        private void ComputeRecommendation()
        {
            var samples = _engine.CalibrationSamples;
            if (samples.Count < 10)
            {
                MessageBox.Show(this,
                    "Presque aucun mouvement n'a été détecté : la mesure n'est pas\n" +
                    "exploitable. Vérifiez que la souris est branchée, puis recommencez.",
                    "Calibrage", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // DÉTREMPAGE : on retire la dérive linéaire de chaque axe (main qui glisse
            // légèrement pendant la mesure) ; le tremblement est l'écart maximal des
            // résidus autour de la droite de dérive. Sans cela, une simple dérive
            // gonflerait le seuil jusqu'à rendre le curseur impossible à déplacer.
            double maxDev = Math.Max(
                ResidualMax(samples, p => p.X),
                ResidualMax(samples, p => p.Y));

            // Seuil : 40 % au-dessus du tremblement mesuré (marge de sécurité),
            // plafonné à 120 px : au-delà, un geste lent mettrait plus d'une seconde
            // à partir et la soupape (2× le seuil) deviendrait trop permissive.
            _recommended.TremorThreshold = Math.Min(120.0, Math.Max(10.0, 1.4 * maxDev));

            // Lissage : plus la main tremble, plus on filtre.
            _recommended.Smoothing = Math.Min(0.9, Math.Max(0.3, 0.3 + maxDev / 40.0));

            // Bande morte : la moitié du tremblement mesuré, bornée à la plage du réglage.
            double deadband = Math.Min(8.0, Math.Max(1.0, maxDev / 2.0));
            _recommended.DeadbandX = deadband;
            _recommended.DeadbandY = deadband;

            MessageBox.Show(this,
                "Tremblement mesuré : ±" + maxDev.ToString("0.#") + " px\n\n" +
                "Seuil recommandé : " + _recommended.TremorThreshold.ToString("0") + "\n" +
                "Lissage recommandé : " + _recommended.Smoothing.ToString("0.00") + "\n" +
                "Bande morte recommandée : " + deadband.ToString("0.#") + " px\n\n" +
                "Après application : la main posée ne doit plus bouger le curseur, et un\n" +
                "geste volontaire doit débloquer le suivi immédiatement.",
                "Réglages recommandés", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Écart maximal des résidus d'un axe autour de sa droite de régression
        /// (moindres carrés, t = index d'échantillon). Isole le tremblement de la
        /// dérive lente de la main.
        /// </summary>
        private static double ResidualMax(System.Collections.Generic.List<NativeMethods.POINT> samples,
                                          Func<NativeMethods.POINT, double> axis)
        {
            int n = samples.Count;
            double sumT = 0, sumV = 0, sumTT = 0, sumTV = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i, v = axis(samples[i]);
                sumT += t; sumV += v; sumTT += t * t; sumTV += t * v;
            }
            double denom = n * sumTT - sumT * sumT;
            double slope = denom != 0 ? (n * sumTV - sumT * sumV) / denom : 0.0;
            double intercept = (sumV - slope * sumT) / n;

            double max = 0;
            for (int i = 0; i < n; i++)
            {
                double resid = Math.Abs(axis(samples[i]) - (slope * i + intercept));
                if (resid > max) max = resid;
            }
            return max;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Sécurité : ne jamais laisser une session de mesure ouverte.
            _engine.StopCalibration();
            base.OnFormClosed(e);
        }
    }
}
