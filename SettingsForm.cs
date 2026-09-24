using System;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Fenêtre de réglages : sensibilité X/Y, lissage, seuil de tremblement,
    /// bande morte, toggle. Les modifications s'appliquent en temps réel ;
    /// Annuler/fermeture restaure le dernier état enregistré.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly SmoothConfig _live;          // appliquée en temps réel
        private readonly SmoothConfig _saved;         // dernier état enregistré
        private readonly MouseHookEngine _engine;     // source d'état du visualiseur

        private CheckBox _chkEnabled, _chkValid;
        private NumericUpDown _numGainX, _numGainY, _numSmooth, _numTremor, _numDeadX, _numDeadY;
        private FilterCrossPanel _visualizer;
        private Button _btnOk, _btnCancel, _btnDefaults;

        /// <summary>Déclenché (thread UI) à chaque application de la configuration.</summary>
        public event Action<SmoothConfig> Changed;

        public SettingsForm(SmoothConfig current, MouseHookEngine engine)
        {
            _live = current.Clone();
            _saved = current.Clone();
            _engine = engine;

            Text = "MouseSmooth — Réglages";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            // Hauteur pilotée par le contenu (AutoSize) : aucun réglage ne peut être
            // masqué par les boutons, quelle que soit la police ou l'échelle DPI.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly;
            MinimumSize = new System.Drawing.Size(460, 0);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 8,
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            _chkEnabled = new CheckBox { Text = "Lissage activé", AutoSize = true };
            table.Controls.Add(_chkEnabled, 0, 0);
            table.SetColumnSpan(_chkEnabled, 3);

            _chkValid = new CheckBox
            {
                Text = "Utilisateur valide — aucun traitement, profil tremblant conservé",
                AutoSize = true
            };
            table.Controls.Add(_chkValid, 0, 1);
            table.SetColumnSpan(_chkValid, 3);

            _numGainX = AddSliderRow(table, 2, "Sensibilité X", 0.1m, 3m, 0.05m);
            _numGainY = AddSliderRow(table, 3, "Sensibilité Y", 0.1m, 3m, 0.05m);
            _numSmooth = AddSliderRow(table, 4, "Lissage (0 = aucun, 0,95 = max)", 0m, 0.95m, 0.05m);
            _numDeadX = AddSliderRow(table, 5, "Bande morte X (px)", 0m, 20m, 0.5m);
            _numDeadY = AddSliderRow(table, 6, "Bande morte Y (px)", 0m, 20m, 0.5m);
            _numTremor = AddSliderRow(table, 7, "Seuil de tremblement (px/s)", 0m, 200m, 5m);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(12, 0, 12, 12)
            };
            _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
            _btnCancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Width = 80 };
            _btnDefaults = new Button { Text = "Défauts", Width = 80 };
            buttons.Controls.Add(_btnOk);
            buttons.Controls.Add(_btnCancel);
            buttons.Controls.Add(_btnDefaults);

            // Ordre d'ajout = ordre d'ancrage inversé : boutons en bas, visualiseur
            // juste au-dessus, tableau remplit le reste.
            Controls.Add(table);
            Controls.Add(buttons);

            // Visualiseur en croix : positions engagée/virtuelle par axe, zone de
            // gel (seuil), portes et valeurs numériques — rafraîchi à 20 Hz.
            _visualizer = new FilterCrossPanel(
                () => _engine.LastState,
                () => _engine.CurrentConfig,
                () => _engine.Enabled,
                () => _engine.PassThrough)
            {
                Dock = DockStyle.Bottom
            };
            Controls.Add(_visualizer);

            var uiTimer = new Timer { Interval = 50 };
            uiTimer.Tick += (s, e) =>
            {
                if (IsDisposed) { uiTimer.Stop(); return; } // fenêtre fermée : arrêt propre
                _visualizer.Invalidate();
            };
            uiTimer.Start();
            FormClosed += (s, e) => uiTimer.Stop();

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            LoadFromConfig(_saved);
            BindLiveEvents();

            _btnOk.Click += (s, e) => { SettingsStore.Save(_live); Close(); };
            _btnCancel.Click += (s, e) =>
            {
                _live.CopyFrom(_saved);
                Changed?.Invoke(_live);
                Close();
            };
            _btnDefaults.Click += (s, e) =>
            {
                _live.ResetToDefaults();
                LoadFromConfig(_live);
                Changed?.Invoke(_live);
            };

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    _live.CopyFrom(_saved);
                    Changed?.Invoke(_live);
                }
            };
        }

        private NumericUpDown AddSliderRow(TableLayoutPanel table, int row, string label,
            decimal min, decimal max, decimal increment)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);

            var track = new TrackBar
            {
                Minimum = (int)(min * 100),
                Maximum = (int)(max * 100),
                TickFrequency = (int)((max - min) * 100 / 8),
                SmallChange = (int)(increment * 100),
                LargeChange = (int)(increment * 100 * 2),
                Width = 140,
                Anchor = AnchorStyles.Left
            };
            var num = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = 2,
                Width = 70,
                Anchor = AnchorStyles.Left
            };
            track.ValueChanged += (s, e) =>
            {
                num.Value = track.Value / 100m;
                num.Focus(); // déclenche la validation du NumericUpDown
                track.Focus();
            };
            num.ValueChanged += (s, e) => { if (track.Value != (int)(num.Value * 100)) track.Value = (int)(num.Value * 100); };

            table.Controls.Add(track, 1, row);
            table.Controls.Add(num, 2, row);
            return num;
        }

        private void BindLiveEvents()
        {
            _chkEnabled.CheckedChanged += (s, e) => { _live.Enabled = _chkEnabled.Checked; UpdateEnabledState(); Changed?.Invoke(_live); };
            _chkValid.CheckedChanged += (s, e) => { _live.ValidMode = _chkValid.Checked; UpdateEnabledState(); Changed?.Invoke(_live); };
            _numGainX.ValueChanged += (s, e) => { _live.GainX = (double)_numGainX.Value; Changed?.Invoke(_live); };
            _numGainY.ValueChanged += (s, e) => { _live.GainY = (double)_numGainY.Value; Changed?.Invoke(_live); };
            _numSmooth.ValueChanged += (s, e) => { _live.Smoothing = (double)_numSmooth.Value; Changed?.Invoke(_live); };
            _numTremor.ValueChanged += (s, e) => { _live.TremorThreshold = (double)_numTremor.Value; Changed?.Invoke(_live); };
            _numDeadX.ValueChanged += (s, e) => { _live.DeadbandX = (double)_numDeadX.Value; Changed?.Invoke(_live); };
            _numDeadY.ValueChanged += (s, e) => { _live.DeadbandY = (double)_numDeadY.Value; Changed?.Invoke(_live); };
        }

        /// <summary>
        /// Grise les réglages quand ils n'ont pas d'effet (lissage désactivé ou mode
        /// utilisateur valide). Les valeurs restent stockées et retrouvent leur effet
        /// dès la sortie du mode.
        /// </summary>
        private void UpdateEnabledState()
        {
            bool processing = _chkEnabled.Checked && !_chkValid.Checked;
            _numGainX.Enabled = _numGainY.Enabled = _numSmooth.Enabled = processing;
            _numDeadX.Enabled = _numDeadY.Enabled = _numTremor.Enabled = processing;
        }

        private void LoadFromConfig(SmoothConfig cfg)
        {
            _chkEnabled.Checked = cfg.Enabled;
            _chkValid.Checked = cfg.ValidMode;
            _numGainX.Value = (decimal)cfg.GainX;
            _numGainY.Value = (decimal)cfg.GainY;
            _numSmooth.Value = (decimal)cfg.Smoothing;
            _numTremor.Value = (decimal)cfg.TremorThreshold;
            _numDeadX.Value = (decimal)cfg.DeadbandX;
            _numDeadY.Value = (decimal)cfg.DeadbandY;
            UpdateEnabledState();
        }

        /// <summary>Réaligne la case à cocher quand le toggle change ailleurs (menu/tray/Ctrl+Alt+M).</summary>
        public void SyncToggle(bool enabled)
        {
            if (!IsDisposed && _chkEnabled.Checked != enabled)
                _chkEnabled.Checked = enabled;
        }

        /// <summary>Recharge l'affichage et l'état de référence après une modification externe (calibrage).</summary>
        public void Reload(SmoothConfig cfg)
        {
            if (IsDisposed) return;
            _saved.CopyFrom(cfg);
            _live.CopyFrom(cfg);
            LoadFromConfig(_live);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.M))
            {
                MessageBox.Show(this,
                    "Ctrl+Alt+M bascule le lissage depuis n'importe quelle application.\n" +
                    "Double-clic sur l'icône systray : ouvrir les réglages.\n\n" +
                    "Seuil de tremblement : en dessous de cette vitesse (px/s), le curseur\n" +
                    "reste immobile — au-dessus, le pointage reprend avec la vitesse native.",
                    "Raccourci", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal static class SmoothConfigExtensions
    {
        /// <summary>Copie champ à champ (les champs sont des propriétés, pas des readonly).</summary>
        public static void CopyFrom(this SmoothConfig target, SmoothConfig source)
        {
            target.Enabled = source.Enabled;
            target.ValidMode = source.ValidMode;
            target.GainX = source.GainX;
            target.GainY = source.GainY;
            target.Smoothing = source.Smoothing;
            target.TremorThreshold = source.TremorThreshold;
            target.DeadbandX = source.DeadbandX;
            target.DeadbandY = source.DeadbandY;
        }
    }
}
