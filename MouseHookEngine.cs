using System;
using System.Runtime.InteropServices;

namespace MouseSmooth
{
    /// <summary>
    /// Installe le hook souris bas niveau (WH_MOUSE_LL) et réinjecte une position
    /// lissée via SetCursorPos. Le hook est installé sur le thread UI (qui pompe
    /// les messages, condition requise pour les hooks bas niveau) : toute la
    /// logique reste mono-thread, sans verrous.
    /// </summary>
    internal sealed class MouseHookEngine : IDisposable
    {
        private readonly IntPtr _module;
        private NativeMethods.LowLevelMouseProc _proc; // référence gardée : sinon GC du délégué du hook
        private IntPtr _hook;
        private SmoothConfig _config;
        private FilterState _state;
        private bool _passThrough;
        private volatile bool _enabled;
        private int _setCursorFailures;

        /// <summary>Appelé (thread UI) si le hook s'est auto-désactivé.</summary>
        public Action AutoDisabled;

        public MouseHookEngine(SmoothConfig config)
        {
            _config = config.Clone();
            _module = NativeMethods.GetModuleHandle(null);
        }

        public bool Enabled => _enabled;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = HookProc;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, _module, 0);
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        public void SetEnabled(bool on)
        {
            _enabled = on;
            _state = default; // ré-ancrage sur la position réelle du curseur au prochain événement
            _setCursorFailures = 0;
        }

        /// <summary>
        /// Applique une nouvelle configuration (thread UI). Si tout est au réglage
        /// "natif" (pas de lissage, pas de seuil, gains à 1, pas de bande morte), le
        /// hook laisse passer le matériel tel quel sans la moindre réinjection.
        /// </summary>
        public void ApplyConfig(SmoothConfig config)
        {
            _config = config.Clone();

            _passThrough =
                _config.Smoothing <= 0.0 &&
                _config.TremorThreshold <= 0.0 &&
                Math.Abs(_config.GainX - 1.0) < 1e-9 &&
                Math.Abs(_config.GainY - 1.0) < 1e-9 &&
                _config.DeadbandX <= 0.0 &&
                _config.DeadbandY <= 0.0;
        }

        /// <summary>
        /// Callback du hook, appelé par Windows pour CHAQUE événement souris du système.
        /// Contrat : rapide (le système impose un délai), jamais d'exception, et renvoyer
        /// 1 seulement pour les événements que l'on réinjecte nous-mêmes.
        /// </summary>
        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            bool blocked = false;
            try
            {
                // On ne traite que : hook actif, traitement utile, et uniquement les
                // déplacements (WM_MOUSEMOVE). Les clics/molette sont laissés au système.
                if (nCode >= 0 && _enabled && !_passThrough &&
                    wParam == (IntPtr)NativeMethods.WM_MOUSEMOVE)
                {
                    // Lecture du MSLLHOOKSTRUCT via offsets : pt.x @0, pt.y @4,
                    // flags @12, time @16. Les flags trahissent les événements injectés
                    // (auto-clickers, autres hooks) : on ne doit JAMAIS les filtrer,
                    // sinon on bouclerait sur nos propres SetCursorPos.
                    uint flags = (uint)Marshal.ReadInt32(lParam, 12);
                    if ((flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) == 0)
                    {
                        int x = Marshal.ReadInt32(lParam, 0);
                        int y = Marshal.ReadInt32(lParam, 4);
                        uint time = (uint)Marshal.ReadInt32(lParam, 16);
                        blocked = ProcessMove(x, y, time);
                    }
                }
            }
            catch
            {
                // Une exception qui sortirait du hook ferait planter le processus :
                // on laisse simplement passer l'événement.
                blocked = false;
            }

            return blocked ? (IntPtr)1 : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Traitement d'un déplacement brut : alimente le filtre puis applique la
        /// cible lissée via SetCursorPos. Renvoie true pour consommer l'événement
        /// original (c'est nous qui positionnons le curseur).
        /// </summary>
        private bool ProcessMove(int px, int py, uint time)
        {
            if (!_state.HasPosition)
            {
                // L'événement va être consommé : on s'ancre d'abord sur la position
                // réelle actuelle du curseur, puis on traite le déplacement brut.
                if (!NativeMethods.GetCursorPos(out NativeMethods.POINT anchor)) return false;
                _state = FilterState.Anchored(anchor.X, anchor.Y, time);
            }

            if (!MouseFilter.Step(ref _state, _config, px, py, time, out int tx, out int ty))
                return true; // événement consommé, mais pas de déplacement à appliquer

            if (NativeMethods.SetCursorPos(tx, ty))
            {
                _setCursorFailures = 0;
            }
            else if (++_setCursorFailures > 200)
            {
                // Le repositionnement échoue en continu : on rend la main à l'utilisateur.
                SetEnabled(false);
                AutoDisabled?.Invoke();
            }
            return true;
        }

        public void Dispose() => Stop();
    }
}
