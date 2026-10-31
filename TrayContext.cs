using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Contexte applicatif : icône systray, menu (toggle, réglages, calibrage,
    /// quitter), raccourci global Ctrl+Alt+M, moteur de lissage.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly SmoothConfig _config;
        private readonly MotionEngine _engine;
        private readonly HiddenWindow _window;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _miEnabled;
        private SettingsForm _settings;
        private Icon _currentIcon;   // icône affichée (détenue par nous, jamais lue via _tray.Icon)
        private bool _exiting;       // garde : aucun callback visuel pendant la fermeture

        public TrayContext()
        {
            _config = SettingsStore.Load();

            _window = new HiddenWindow();
            _window.HotkeyPressed += Toggle;
            _window.Show(); // garde le handle vivant (raccourci global, marshaling vers l'UI)

            _engine = new MotionEngine(_config);
            _engine.AutoDisabled += () =>
            {
                // Déclenché sur le thread moteur : retour au thread UI.
                try { _window.BeginInvoke((Action)OnEngineAutoDisabled); } catch { }
            };

            _tray = new NotifyIcon { Text = TrayText(), Visible = true };
            SwapIcon(MakeIcon(_config.Enabled));
            _tray.DoubleClick += (s, e) => ShowSettings();

            var menu = new ContextMenuStrip();
            _miEnabled = new ToolStripMenuItem("Lissage activé  (Ctrl+Alt+M)") { Checked = _config.Enabled };
            _miEnabled.Click += (s, e) => Toggle();
            menu.Items.Add(_miEnabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Réglages…", null, (s, e) => ShowSettings());
            menu.Items.Add("Calibrage…", null, (s, e) => ShowCalibration());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());
            _tray.ContextMenuStrip = menu;

            if (!_window.RegisterToggleHotkey())
            {
                MessageBox.Show("Le raccourci global Ctrl+Alt+M n'a pas pu être réservé " +
                                "(utilisé par une autre application ?).",
                    "MouseSmooth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (!_engine.Start())
            {
                MessageBox.Show("Impossible d'installer le hook souris : le lissage ne fonctionnera pas.",
                    "MouseSmooth", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Bascule lissage actif/inactif (menu, Ctrl+Alt+M, réglages).</summary>
        private void Toggle() => SetEnabled(!_config.Enabled);

        private void SetEnabled(bool on)
        {
            _config.Enabled = on;
            _engine.SetEnabled(on);
            RefreshVisuals();
            SettingsStore.Save(_config);
        }

        private string TrayText() =>
            _config.Enabled ? "MouseSmooth — lissage actif" : "MouseSmooth — souris native";

        /// <summary>Remplace l'icône du tray en libérant proprement l'ancienne.</summary>
        private void SwapIcon(Icon newIcon)
        {
            Icon old = _currentIcon;
            _currentIcon = newIcon;
            _tray.Icon = newIcon;
            if (old != null)
            {
                try { NativeMethods.DestroyIcon(old.Handle); }
                catch { /* handle déjà invalide : rien à faire */ }
                old.Dispose();
            }
        }

        /// <summary>Met à jour icône, info-bulle, menu et fenêtre de réglages selon la config.</summary>
        private void RefreshVisuals()
        {
            if (_exiting) return;
            SwapIcon(MakeIcon(_config.Enabled));
            _tray.Text = TrayText();
            _miEnabled.Checked = _config.Enabled;
            _settings?.SyncEnabled(_config.Enabled);
        }

        /// <summary>Applique une configuration venant des réglages ou du calibrage.</summary>
        private void Apply(SmoothConfig cfg)
        {
            bool toggled = cfg.Enabled != _config.Enabled;
            _config.CopyFrom(cfg);
            _engine.ApplyConfig(_config);
            if (toggled) _engine.SetEnabled(_config.Enabled);
            RefreshVisuals();
        }

        private void ShowCalibration()
        {
            using (var form = new CalibrationForm(_engine, _config))
            {
                form.Applied += cfg =>
                {
                    Apply(cfg);
                    SettingsStore.Save(_config);
                    _settings?.Reload(_config);
                };
                form.ShowDialog();
            }
        }

        /// <summary>Ouvre (ou ramène au premier plan) la fenêtre de réglages.</summary>
        private void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(_config, _engine);
                _settings.Changed += Apply;
                _settings.CalibrateRequested += ShowCalibration;
            }
            _settings.Show();
            _settings.Activate();
        }

        /// <summary>Désactivation de secours déclenchée par le moteur (curseur non repositionnable).</summary>
        private void OnEngineAutoDisabled()
        {
            if (_exiting) return;
            _config.Enabled = false;
            RefreshVisuals();
            SettingsStore.Save(_config);
            _tray.ShowBalloonTip(4000, "MouseSmooth désactivé",
                "Le curseur n'a pas pu être repositionné de façon répétée (session verrouillée, " +
                "bureau sécurisé…). Réactivez le lissage avec Ctrl+Alt+M.", ToolTipIcon.Warning);
        }

        /// <summary>
        /// Fermeture ordonnée et idempotente : icône retirée d'abord, puis moteur
        /// (hook), raccourci, fenêtres. Chaque étape est tolérante aux pannes.
        /// </summary>
        private void Exit()
        {
            if (_exiting) return;
            _exiting = true;
            TearDown();
            Application.Exit();
        }

        private void TearDown()
        {
            try { _tray.Visible = false; } catch { }
            try { _engine.Dispose(); } catch { }
            try { _window.UnregisterHotkeySafe(); } catch { }
            try { _settings?.Close(); } catch { }
            try { _tray.Dispose(); } catch { }
            try
            {
                if (_currentIcon != null)
                {
                    NativeMethods.DestroyIcon(_currentIcon.Handle);
                    _currentIcon.Dispose();
                    _currentIcon = null;
                }
            }
            catch { }
        }

        /// <summary>Icône générée à la volée : pointeur blanc sur pastille verte (actif) ou grise (natif).</summary>
        private static Icon MakeIcon(bool enabled)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color bg = enabled ? Color.FromArgb(46, 160, 67) : Color.FromArgb(110, 110, 110);
                using (var brush = new SolidBrush(bg))
                    g.FillEllipse(brush, 1, 1, 30, 30);
                // Flèche de pointeur.
                var arrow = new[]
                {
                    new Point(10, 6), new Point(10, 25), new Point(15, 20),
                    new Point(19, 27), new Point(22, 25), new Point(18, 18), new Point(24, 18)
                };
                g.FillPolygon(Brushes.White, arrow);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting)
            {
                _exiting = true;
                TearDown();
            }
            base.Dispose(disposing);
        }
    }
}
