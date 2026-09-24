using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>Fenêtre jamais affichée : réception du raccourci global + marshaling UI.</summary>
    internal sealed class HiddenWindow : Form
    {
        public const int HotkeyId = 0xB00B;

        public event Action HotkeyPressed;

        public HiddenWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW : absent d'Alt-Tab
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
            }
            base.WndProc(ref m);
        }

        public bool RegisterToggleHotkey()
        {
            var handle = Handle; // force la création du handle
            return NativeMethods.RegisterHotKey(handle, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, (uint)'M');
        }

        /// <summary>Désenregistre le raccourci sans lever d'exception (fenêtre déjà détruite, etc.).</summary>
        public void UnregisterHotkeySafe()
        {
            try
            {
                if (IsHandleCreated)
                    NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            }
            catch
            {
                // La fenêtre est déjà partie : rien à faire.
            }
        }
    }
}
