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

        public TrayContext()
        {
            _config = SettingsStore.Load();
            _engine = new MouseHookEngine(_config);
            _engine.ApplyConfig(_config);
            _engine.AutoDisabled = OnEngineAutoDisabled;

            _window = new HiddenWindow();
            _window.HotkeyPressed += Toggle;
            _window.Show(); // garde le handle vivant pour le raccourci global (fenêtre hors écran)

            Icon icon = MakeIcon(_config.Enabled);
            _tray = new NotifyIcon
            {
                Icon = icon,
                Text = "MouseSmooth — actif",
                Visible = true
            };
            _tray.DoubleClick += (s, e) => ShowSettings();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Activé", null, (s, e) => Toggle());
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
            Icon oldIcon = _tray.Icon;
            _tray.Icon = MakeIcon(_config.Enabled);
            if (oldIcon != null) { NativeMethods.DestroyIcon(oldIcon.Handle); oldIcon.Dispose(); }
            _tray.Text = _config.Enabled ? "MouseSmooth — actif" : "MouseSmooth — désactivé";
            if (_tray.ContextMenuStrip != null)
                ((ToolStripMenuItem)_tray.ContextMenuStrip.Items[0]).Checked = _config.Enabled;
            SettingsStore.Save(_config);
            _settings?.SyncToggle(_config.Enabled);
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
                _settings = new SettingsForm(_config);
                _settings.Changed += cfg =>
                {
                    _engine.ApplyConfig(cfg);
                    _engine.SetEnabled(cfg.Enabled);
                    Icon oldIcon = _tray.Icon;
                    _tray.Icon = MakeIcon(cfg.Enabled);
                    if (oldIcon != null) { NativeMethods.DestroyIcon(oldIcon.Handle); oldIcon.Dispose(); }
                    _tray.Text = cfg.Enabled ? "MouseSmooth — actif" : "MouseSmooth — désactivé";
                    if (_tray.ContextMenuStrip != null)
                        ((ToolStripMenuItem)_tray.ContextMenuStrip.Items[0]).Checked = cfg.Enabled;
                };
            }
            _settings.Show();
            _settings.Activate();
        }

        /// <summary>Désactivation de secours déclenchée par le moteur (curseur non repositionnable).</summary>
        private void OnEngineAutoDisabled()
        {
            _config.Enabled = false;
            Icon oldIcon = _tray.Icon;
            _tray.Icon = MakeIcon(false);
            if (oldIcon != null) { NativeMethods.DestroyIcon(oldIcon.Handle); oldIcon.Dispose(); }
            _tray.Text = "MouseSmooth — désactivé";
            if (_tray.ContextMenuStrip != null)
                ((ToolStripMenuItem)_tray.ContextMenuStrip.Items[0]).Checked = false;
            SettingsStore.Save(_config);
            _tray.ShowBalloonTip(4000, "MouseSmooth désactivé",
                "Le repositionnement du curseur a échoué de façon répétée (session verrouillée, " +
                "bureau sécurisé…). Le lissage est désactivé.", ToolTipIcon.Warning);
        }

        private void Exit()
        {
            Icon current = _tray.Icon;
            if (current != null) { NativeMethods.DestroyIcon(current.Handle); current.Dispose(); }
            _engine.Dispose();
            _window.UnregisterHotkeySafe();
            _tray.Visible = false;
            _tray.Dispose();
            _settings?.Close();
            Application.Exit();
        }

        /// <summary>Icône systray générée à la volée : pointeur blanc sur pastille verte/rouge.</summary>
        private static Icon MakeIcon(bool enabled)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(enabled ? Color.FromArgb(46, 160, 67) : Color.FromArgb(170, 40, 40)))
                    g.FillEllipse(bg, 1, 1, 30, 30);
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
            if (disposing)
            {
                _engine.Dispose();
                _window.UnregisterHotkeySafe();
                _tray.Visible = false;
                _tray.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
