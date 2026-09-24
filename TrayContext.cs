using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Contexte applicatif : icône systray, menu (toggle, réglages, quitter),
    /// raccourci global Ctrl+Alt+M, moteur de hook.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext, IDisposable
    {
        private readonly SmoothConfig _config;
        private readonly MouseHookEngine _engine;
        private readonly HiddenWindow _window;
        private readonly NotifyIcon _tray;
        private SettingsForm _settings;
        private ToolStripMenuItem _miValidMode;
        private Icon _currentIcon;   // icône affichée (détenue par nous, jamais via _tray.Icon)
        private bool _exiting;       // garde : aucun callback visuel pendant la fermeture

        public TrayContext()
        {
            _config = SettingsStore.Load();
            _engine = new MouseHookEngine(_config);
            _engine.ApplyConfig(_config);
            _engine.AutoDisabled = OnEngineAutoDisabled;

            _window = new HiddenWindow();
            _window.HotkeyPressed += Toggle;
            _window.Show(); // garde le handle vivant pour le raccourci global (fenêtre hors écran)

            _tray = new NotifyIcon
            {
                Text = TrayText(),
                Visible = true
            };
            SwapIcon(MakeIcon(_config.Enabled, _config.ValidMode));
            _tray.DoubleClick += (s, e) => ShowSettings();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Activé", null, (s, e) => Toggle());
            _miValidMode = new ToolStripMenuItem("Utilisateur valide") { Checked = _config.ValidMode };
            _miValidMode.Click += (s, e) => SetValidMode(!_config.ValidMode);
            menu.Items.Add(_miValidMode);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Réglages…", null, (s, e) => ShowSettings());
            menu.Items.Add("Calibrage…", null, (s, e) => ShowCalibration());
            menu.Items.Add("Quitter", null, (s, e) => Exit());
            _tray.ContextMenuStrip = menu;

            // Item "Activé" coché selon l'état.
            ((ToolStripMenuItem)menu.Items[0]).Checked = _config.Enabled;

            if (!_window.RegisterToggleHotkey())
            {
                MessageBox.Show("Le raccourci global Ctrl+Alt+M n'a pas pu être réservé " +
                                "(utilisé par une autre application ?).",
                    "MouseSmooth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (_config.Enabled) _engine.SetEnabled(true);
            _engine.Start();
        }

        /// <summary>Bascule lissage actif/inactif (menu, double-clic icône, Ctrl+Alt+M).</summary>
        private void Toggle()
        {
            _config.Enabled = !_config.Enabled;
            _engine.SetEnabled(_config.Enabled);
            RefreshVisuals();
            SettingsStore.Save(_config);
        }

        /// <summary>Texte d'info-bulle selon l'état (utilisateur valide / actif / désactivé).</summary>
        private string TrayText() =>
            _config.ValidMode ? "MouseSmooth — utilisateur valide (natif)"
            : _config.Enabled ? "MouseSmooth — actif"
            : "MouseSmooth — désactivé";

        /// <summary>
        /// Remplace l'icône du tray en libérant proprement l'ancienne. L'icône
        /// courante est suivie dans un champ : on ne lit jamais _tray.Icon, qui peut
        /// être momentanément incohérent (source d'exceptions à la fermeture).
        /// </summary>
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

        /// <summary>Met à jour icône, info-bulle et coches du menu selon la config.</summary>
        private void RefreshVisuals()
        {
            if (_exiting) return; // fermeture en cours : ne plus toucher au tray
            SwapIcon(MakeIcon(_config.Enabled, _config.ValidMode));
            _tray.Text = TrayText();
            if (_tray.ContextMenuStrip != null)
            {
                ((ToolStripMenuItem)_tray.ContextMenuStrip.Items[0]).Checked = _config.Enabled;
                ((ToolStripMenuItem)_tray.ContextMenuStrip.Items[1]).Checked = _config.ValidMode;
            }
            _settings?.SyncToggle(_config.Enabled);
        }

        /// <summary>
        /// Mode utilisateur valide : le hook RESTE installé mais ne traite plus rien
        /// (pass-through natif) ; le profil tremblant est conservé intact et revient
        /// dès la sortie du mode. Bascule instantanée, sans réinstallation.
        /// </summary>
        private void SetValidMode(bool on)
        {
            _config.ValidMode = on;
            _engine.ApplyConfig(_config);
            SettingsStore.Save(_config);
            RefreshVisuals();
            _settings?.Reload(_config);
        }

        /// <summary>
        /// Assistant de calibrage : mesure le tremblement réel puis propose des
        /// réglages. S'ils sont appliqués, ils prennent effet immédiatement.
        /// </summary>
        private void ShowCalibration()
        {
            using (var form = new CalibrationForm(_engine, _config))
            {
                form.Applied += cfg =>
                {
                    _config.CopyFrom(cfg);
                    _engine.ApplyConfig(_config);
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
                _settings.Changed += cfg =>
                {
                    _config.CopyFrom(cfg); // répercute les valeurs du formulaire
                    _engine.ApplyConfig(cfg);
                    _engine.SetEnabled(cfg.Enabled);
                    RefreshVisuals();
                };
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
                "Le repositionnement du curseur a échoué de façon répétée (session verrouillée, " +
                "bureau sécurisé…). Le lissage est désactivé.", ToolTipIcon.Warning);
        }

        /// <summary>
        /// Fermeture ordonnée et idempotente : d'abord retirer l'icône du tray (plus
        /// aucune interaction NIM possible), puis le hook, le raccourci, les fenêtres,
        /// et enfin l'icône détenue. Chaque étape est tolérante aux pannes.
        /// </summary>
        private void Exit()
        {
            if (_exiting) return;
            _exiting = true;

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

            Application.Exit();
        }

        /// <summary>
        /// Icône systray générée à la volée (pointeur blanc sur pastille) :
        /// bleue = utilisateur valide (natif), verte = actif, rouge = désactivé.
        /// </summary>
        private static Icon MakeIcon(bool enabled, bool validMode)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color bg = validMode ? Color.FromArgb(31, 111, 235)
                         : enabled   ? Color.FromArgb(46, 160, 67)
                         : Color.FromArgb(170, 40, 40);
                using (var brush = new SolidBrush(bg))
                    g.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new Pen(Color.White, 3f))
                {
                    g.DrawLine(pen, 7, 9, 7, 23);                       // tige verticale
                    g.DrawArc(pen, 2, 5, 10, 10, 180, 180);             // arrondi haut de la tige
                    g.DrawLine(pen, 16, 8, 16, 23);                     // tige verticale
                    g.DrawArc(pen, 11, 4, 10, 10, 180, 180);            // arrondi haut de la tige
                    g.DrawLine(pen, 23, 12, 23, 23);                    // petite tige
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting)
            {
                // Chemin de secours (disposition sans passage par Quitter) : le même
                // démontage, protégé ; si Exit() a déjà tourné, rien à refaire.
                try { _tray.Visible = false; } catch { }
                try { _engine.Dispose(); } catch { }
                try { _window.UnregisterHotkeySafe(); } catch { }
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
            base.Dispose(disposing);
        }
    }
}
