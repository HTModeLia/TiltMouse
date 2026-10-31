using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Assistant de calibrage : la main repose sur la souris, sans intention de
    /// mouvement, pendant 6 secondes. Le mouvement de la main est enregistré à
    /// 250 Hz, puis analysé : amplitude et fréquence du tremblement. La durée de
    /// moyenne recommandée est UNE période du tremblement, durée pour laquelle la
    /// moyenne glissante annule exactement l'oscillation.
    /// </summary>
    internal sealed class CalibrationForm : Form
    {
        private const int CountdownTicks = 30; // 3 s à 100 ms
        private const int MeasureTicks = 60;   // 6 s à 100 ms

        private enum Phase { Idle, Countdown, Measuring }

        private readonly MotionEngine _engine;
        private readonly SmoothConfig _recommended;
        private readonly Timer _timer = new Timer { Interval = 100 };
        private Phase _phase = Phase.Idle;
        private int _ticksLeft;

        private readonly Label _lblMain, _lblResult;
        private readonly ProgressBar _bar;
        private readonly Button _btnStart, _btnApply;

        /// <summary>Déclenché quand l'utilisateur applique les réglages recommandés.</summary>
        public event Action<SmoothConfig> Applied;

        public CalibrationForm(MotionEngine engine, SmoothConfig current)
        {
            _engine = engine;
            _recommended = current.Clone();

            Text = "MouseSmooth — Calibrage";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 270);

            _lblMain = new Label
            {
                Text = "Posez la main sur la souris, détendue, sans chercher à bouger.\n" +
                       "Le tremblement sera mesuré pendant 6 secondes.",
                Location = new Point(16, 14),
                Size = new Size(468, 48)
            };
            _bar = new ProgressBar { Location = new Point(16, 68), Size = new Size(468, 22), Maximum = 100 };
            _lblResult = new Label
            {
                Text = "",
                Location = new Point(16, 98),
                Size = new Size(468, 110),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            _btnStart = new Button { Text = "Démarrer la mesure", AutoSize = true, Location = new Point(16, 222) };
            _btnApply = new Button { Text = "Appliquer", AutoSize = true, Location = new Point(200, 222), Enabled = false };
            var btnClose = new Button { Text = "Fermer", Width = 90, Location = new Point(394, 222), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { _lblMain, _bar, _lblResult, _btnStart, _btnApply, btnClose });
            CancelButton = btnClose;

            _timer.Tick += OnTick;
            _btnStart.Click += (s, e) => StartCountdown();
            _btnApply.Click += (s, e) => { Applied?.Invoke(_recommended); Close(); };
        }

        private void StartCountdown()
        {
            _phase = Phase.Countdown;
            _ticksLeft = CountdownTicks;
            _bar.Value = 0;
            _btnStart.Enabled = false;
            _btnApply.Enabled = false;
            _lblResult.Text = "";
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _ticksLeft--;
            if (_phase == Phase.Countdown)
            {
                _lblMain.Text = "Préparez-vous : posez la main… " + Math.Ceiling(_ticksLeft / 10.0) + " s";
                if (_ticksLeft > 0) return;

                _phase = Phase.Measuring;
                _ticksLeft = MeasureTicks;
                _engine.StartRecording();
                _lblMain.Text = "Mesure en cours — gardez la main posée et détendue…";
                return;
            }

            if (_phase == Phase.Measuring)
            {
                _bar.Value = Math.Min(100, (MeasureTicks - _ticksLeft) * 100 / MeasureTicks);
                if (_ticksLeft > 0) return;

                _timer.Stop();
                _phase = Phase.Idle;
                ShowResult(TremorAnalysis.Analyze(_engine.StopRecording(), TremorFilter.TickMs));
                _btnStart.Text = "Recommencer";
                _btnStart.Enabled = true;
            }
        }

        private void ShowResult(TremorMeasure m)
        {
            _lblMain.Text = "Mesure terminée.";
            if (m.AmplitudePx < 0.5)
            {
                _lblResult.Text = "Aucun mouvement détecté : la main était-elle posée sur la souris ?\n" +
                                  "Recommencez la mesure.";
                return;
            }

            _recommended.SmoothingMs = m.RecommendedMs;
            string freq = m.FrequencyHz > 0
                ? $"Fréquence du tremblement : {m.FrequencyHz:0.0} Hz (une oscillation toutes les {1000 / m.FrequencyHz:0} ms)"
                : "Fréquence : pas de rythme net détecté — durée standard proposée";
            _lblResult.Text =
                $"Amplitude du tremblement : ±{m.AmplitudePx:0.#} px\n" +
                freq + "\n\n" +
                $"Durée de moyenne recommandée : {m.RecommendedMs:0} ms\n" +
                "(le curseur suivra la main avec environ ce retard, sans le tremblement)";
            _btnApply.Enabled = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Ne jamais laisser un enregistrement ouvert.
            _timer.Stop();
            _timer.Dispose();
            _engine.StopRecording();
            base.OnFormClosed(e);
        }
    }
}
