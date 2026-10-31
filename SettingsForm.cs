using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Fenêtre de réglages : les modifications s'appliquent en temps réel (on voit
    /// l'effet dans le visualiseur et sur le curseur) ; OK enregistre, Annuler ou
    /// la fermeture restaurent l'état enregistré.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly SmoothConfig _live;   // appliquée en temps réel
        private readonly SmoothConfig _saved;  // dernier état enregistré
        private bool _loading;

        private readonly CheckBox _chkEnabled;
        private readonly Slider _smoothing, _reactivity, _speed, _click;

        /// <summary>Déclenché (thread UI) à chaque modification.</summary>
        public event Action<SmoothConfig> Changed;

        /// <summary>Le bouton « Calibrage… » a été cliqué.</summary>
        public event Action CalibrateRequested;

        public SettingsForm(SmoothConfig current, MotionEngine engine)
        {
            _live = current.Clone();
            _saved = current.Clone();

            Text = "MouseSmooth — Réglages";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                Padding = new Padding(12),
                Dock = DockStyle.Fill
            };

            _chkEnabled = new CheckBox { Text = "Lissage activé   (Ctrl+Alt+M depuis n'importe où)", AutoSize = true };
            AddFullRow(layout, _chkEnabled);

            _smoothing = new Slider(layout, "Durée de la moyenne", "ms", 0, 500, 10, 0,
                "Le curseur suit la MOYENNE de la main sur cette durée. Idéal : la période du " +
                "tremblement (200 ms pour 5 Hz) — utilisez le calibrage. Plus long = plus stable, " +
                "mais le curseur suit avec plus de retard. 0 = aucun lissage.");
            _reactivity = new Slider(layout, "Réactivité aux grands gestes", "%", 0, 100, 5, 0,
                "Pendant un déplacement franc, la moyenne est raccourcie pour réduire le retard. " +
                "Le pointage lent et précis reste toujours entièrement lissé.");
            _speed = new Slider(layout, "Vitesse du curseur", "×", 0.25m, 3m, 0.05m, 2,
                "1,00 = vitesse Windows. Une vitesse plus faible rend aussi le tremblement plus petit à l'écran.");
            _click = new Slider(layout, "Stabilisation des clics", "ms", 0, 300, 10, 0,
                "Les mouvements de la main sont ignorés pendant cette durée après chaque appui " +
                "ou relâchement de bouton : la pression du doigt ne déplace plus le curseur.");

            var view = new TrajectoryView(engine, () => _live)
            {
                Size = new Size(560, 230),
                Margin = new Padding(0, 10, 0, 6),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            AddFullRow(layout, view);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var btnOk = new Button { Text = "OK", Width = 90 };
            var btnCancel = new Button { Text = "Annuler", Width = 90 };
            var btnDefaults = new Button { Text = "Valeurs par défaut", AutoSize = true };
            var btnCalibrate = new Button { Text = "Calibrage…", AutoSize = true };
            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnDefaults);
            buttons.Controls.Add(btnCalibrate);
            AddFullRow(layout, buttons);

            Controls.Add(layout);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            LoadFromConfig(_live);

            _chkEnabled.CheckedChanged += (s, e) => Change(c => c.Enabled = _chkEnabled.Checked);
            _smoothing.ValueChanged += v => Change(c => c.SmoothingMs = v);
            _reactivity.ValueChanged += v => Change(c => c.Reactivity = v / 100.0);
            _speed.ValueChanged += v => Change(c => c.Speed = v);
            _click.ValueChanged += v => Change(c => c.ClickStabilizeMs = v);

            btnOk.Click += (s, e) =>
            {
                SettingsStore.Save(_live);
                _saved.CopyFrom(_live);
                Close();
            };
            btnCancel.Click += (s, e) => Close();
            btnDefaults.Click += (s, e) =>
            {
                bool enabled = _live.Enabled;
                _live.ResetToDefaults();
                _live.Enabled = enabled;
                LoadFromConfig(_live);
                Changed?.Invoke(_live);
            };
            btnCalibrate.Click += (s, e) => CalibrateRequested?.Invoke();

            // Fermeture sans OK : retour à l'état enregistré.
            FormClosing += (s, e) =>
            {
                if (!SameAs(_live, _saved))
                {
                    _live.CopyFrom(_saved);
                    Changed?.Invoke(_live);
                }
            };
        }

        private static void AddFullRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 3);
        }

        private void Change(Action<SmoothConfig> change)
        {
            if (_loading) return;
            change(_live);
            Changed?.Invoke(_live);
        }

        private void LoadFromConfig(SmoothConfig cfg)
        {
            _loading = true;
            _chkEnabled.Checked = cfg.Enabled;
            _smoothing.Value = cfg.SmoothingMs;
            _reactivity.Value = cfg.Reactivity * 100.0;
            _speed.Value = cfg.Speed;
            _click.Value = cfg.ClickStabilizeMs;
            _loading = false;
        }

        private static bool SameAs(SmoothConfig a, SmoothConfig b) =>
            a.Enabled == b.Enabled && a.SmoothingMs == b.SmoothingMs && a.Reactivity == b.Reactivity &&
            a.Speed == b.Speed && a.ClickStabilizeMs == b.ClickStabilizeMs;

        /// <summary>Réaligne la case quand le toggle change ailleurs (menu, Ctrl+Alt+M).</summary>
        public void SyncEnabled(bool enabled)
        {
            if (IsDisposed || _chkEnabled.Checked == enabled) return;
            _live.Enabled = enabled;
            _saved.Enabled = enabled;
            _loading = true;
            _chkEnabled.Checked = enabled;
            _loading = false;
        }

        /// <summary>Recharge après une modification externe (calibrage appliqué et enregistré).</summary>
        public void Reload(SmoothConfig cfg)
        {
            if (IsDisposed) return;
            _saved.CopyFrom(cfg);
            _live.CopyFrom(cfg);
            LoadFromConfig(_live);
        }

        /// <summary>Une ligne de réglage : libellé, curseur, valeur numérique, explication.</summary>
        private sealed class Slider
        {
            private readonly TrackBar _track;
            private readonly NumericUpDown _num;
            private readonly decimal _factor;
            private bool _syncing;

            public event Action<double> ValueChanged;

            public Slider(TableLayoutPanel layout, string label, string unit,
                          decimal min, decimal max, decimal step, int decimals, string help)
            {
                _factor = 1m / step;
                int row = layout.RowCount++;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Margin = new Padding(0, 10, 8, 0)
                }, 0, row);

                _track = new TrackBar
                {
                    Minimum = (int)(min * _factor),
                    Maximum = (int)(max * _factor),
                    TickStyle = TickStyle.None,
                    SmallChange = 1,
                    LargeChange = Math.Max(1, (int)((max - min) * _factor / 10)),
                    Width = 220,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 6, 0, 0)
                };
                layout.Controls.Add(_track, 1, row);

                var numPanel = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
                _num = new NumericUpDown
                {
                    Minimum = min,
                    Maximum = max,
                    Increment = step,
                    DecimalPlaces = decimals,
                    Width = 70
                };
                numPanel.Controls.Add(_num);
                numPanel.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(2, 5, 0, 0) });
                layout.Controls.Add(numPanel, 2, row);

                var helpLabel = new Label
                {
                    Text = help,
                    ForeColor = Color.DimGray,
                    AutoSize = true,
                    MaximumSize = new Size(560, 0),
                    Margin = new Padding(0, 0, 0, 2)
                };
                int helpRow = layout.RowCount++;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(helpLabel, 0, helpRow);
                layout.SetColumnSpan(helpLabel, 3);

                _track.ValueChanged += (s, e) =>
                {
                    if (_syncing) return;
                    _syncing = true;
                    _num.Value = Math.Min(_num.Maximum, Math.Max(_num.Minimum, _track.Value / _factor));
                    _syncing = false;
                    ValueChanged?.Invoke((double)_num.Value);
                };
                _num.ValueChanged += (s, e) =>
                {
                    if (_syncing) return;
                    _syncing = true;
                    _track.Value = Math.Min(_track.Maximum, Math.Max(_track.Minimum, (int)Math.Round(_num.Value * _factor)));
                    _syncing = false;
                    ValueChanged?.Invoke((double)_num.Value);
                };
            }

            public double Value
            {
                get => (double)_num.Value;
                set
                {
                    _syncing = true;
                    decimal v = Math.Min(_num.Maximum, Math.Max(_num.Minimum, (decimal)value));
                    _num.Value = v;
                    _track.Value = Math.Min(_track.Maximum, Math.Max(_track.Minimum, (int)Math.Round(v * _factor)));
                    _syncing = false;
                }
            }
        }
    }
}
