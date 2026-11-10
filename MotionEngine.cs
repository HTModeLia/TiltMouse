using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseSmooth
{
    /// <summary>Un point de la trace du visualiseur : main (V) et curseur lissé.</summary>
    internal struct TracePoint
    {
        public float HandX, HandY, CursorX, CursorY;
    }

    /// <summary>Mesure d'un segment de 3 s d'usage réel (journal du tremblement).</summary>
    internal struct JournalSample
    {
        public double FrequencyHz, AmplitudePx, NetSpeedPxS;
    }

    /// <summary>
    /// Moteur : hook souris bas niveau + horloge à 250 Hz, sur un THREAD DÉDIÉ.
    ///
    /// Le hook bloque les déplacements physiques et transmet la position voulue à
    /// <see cref="MotionCore"/> ; l'horloge fait avancer la moyenne glissante et
    /// place le curseur. L'horloge continue après l'arrêt de la main, le temps que
    /// la moyenne converge : le curseur termine son trajet en douceur, puis le
    /// thread s'endort (zéro CPU au repos).
    ///
    /// Thread dédié : une fenêtre de l'interface occupée ne peut ni retarder le
    /// curseur, ni provoquer le retrait du hook par Windows (délai dépassé).
    /// </summary>
    internal sealed class MotionEngine : IDisposable
    {
        private const uint WM_WAKE = NativeMethods.WM_APP + 1;
        private const int TraceCapacity = 500;       // 2 s à 250 Hz
        private const int RecordCapacity = 4000;     // 16 s à 250 Hz
        private const int MaxSetCursorFailures = 100;
        private const int SegmentTicks = 750;        // 3 s à 250 Hz
        private const int JournalCapacity = 5000;    // borne mémoire si le journal n'est jamais vidé

        private readonly MotionCore _core = new MotionCore();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new object();
        private readonly TracePoint[] _trace = new TracePoint[TraceCapacity];
        private readonly List<(double X, double Y)> _record = new List<(double X, double Y)>(RecordCapacity);
        private readonly List<(double X, double Y)> _segment = new List<(double X, double Y)>(SegmentTicks);
        private readonly List<JournalSample> _journal = new List<JournalSample>();

        private Thread _thread;
        private uint _threadId;
        private NativeMethods.LowLevelMouseProc _proc; // référence gardée : sinon GC du délégué
        private IntPtr _hook;

        // Commandes du thread UI vers le thread moteur.
        private SmoothConfig _pendingConfig;
        private volatile bool _enabled;
        private volatile bool _recording;
        private volatile bool _journalOn;

        // État du thread moteur.
        private SmoothConfig _config = new SmoothConfig();
        private bool _processing;
        private bool _idle = true;
        private double _nextTickMs;
        private int _setCursorFailures;
        private int _traceHead, _traceCount;

        /// <summary>
        /// Déclenché sur le THREAD MOTEUR quand le lissage s'est coupé tout seul
        /// (curseur impossible à repositionner) : à remarshaler vers l'interface.
        /// </summary>
        public event Action AutoDisabled;

        public bool Enabled => _enabled;

        /// <summary>Vitesse volontaire estimée (px/s) : visualiseur.</summary>
        public double NetSpeed { get; private set; }

        /// <summary>Part de moyenne courte utilisée (0..1) : visualiseur.</summary>
        public double ReactiveWeight { get; private set; }

        public MotionEngine(SmoothConfig config)
        {
            _config = config.Clone();
            _core.Configure(_config);
            _enabled = config.Enabled;
        }

        /// <summary>Démarre le thread moteur et installe le hook. Faux si le hook est refusé.</summary>
        public bool Start()
        {
            if (_thread != null) return _hook != IntPtr.Zero;
            using (var ready = new ManualResetEventSlim())
            {
                _thread = new Thread(() => Run(ready))
                {
                    IsBackground = true,
                    Name = "MouseSmooth motion",
                    Priority = ThreadPriority.AboveNormal
                };
                _thread.Start();
                ready.Wait(5000);
            }
            return _hook != IntPtr.Zero;
        }

        public void Stop()
        {
            if (_thread == null) return;
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(2000);
            _thread = null;
        }

        public void Dispose() => Stop();

        public void ApplyConfig(SmoothConfig config)
        {
            Volatile.Write(ref _pendingConfig, config.Clone());
            Wake();
        }

        public void SetEnabled(bool on)
        {
            _enabled = on;
            Wake();
        }

        /// <summary>Calibrage : commence à enregistrer la position de la main à 250 Hz.</summary>
        public void StartRecording()
        {
            lock (_lock) _record.Clear();
            _recording = true;
            Wake();
        }

        /// <summary>Arrête l'enregistrement et renvoie les échantillons.</summary>
        public List<(double X, double Y)> StopRecording()
        {
            _recording = false;
            lock (_lock) return new List<(double X, double Y)>(_record);
        }

        /// <summary>Journal du tremblement : mesure en continu pendant l'usage (lissage actif uniquement).</summary>
        public void SetJournal(bool on) => _journalOn = on;

        /// <summary>Renvoie et vide les mesures accumulées depuis le dernier appel.</summary>
        public List<JournalSample> DrainJournal()
        {
            lock (_lock)
            {
                var result = new List<JournalSample>(_journal);
                _journal.Clear();
                return result;
            }
        }

        /// <summary>Copie la trace récente (du plus ancien au plus récent) : visualiseur.</summary>
        public void CopyTrace(List<TracePoint> destination)
        {
            destination.Clear();
            lock (_lock)
            {
                int start = (_traceHead - _traceCount + TraceCapacity) % TraceCapacity;
                for (int i = 0; i < _traceCount; i++)
                    destination.Add(_trace[(start + i) % TraceCapacity]);
            }
        }

        private void Wake()
        {
            if (_threadId != 0)
                NativeMethods.PostThreadMessage(_threadId, WM_WAKE, IntPtr.Zero, IntPtr.Zero);
        }

        private double NowMs => _clock.Elapsed.TotalMilliseconds;

        // ------------------------------------------------------------------
        // Thread moteur
        // ------------------------------------------------------------------

        private void Run(ManualResetEventSlim ready)
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE); // crée la file de messages

            _proc = HookProc;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc,
                                                   NativeMethods.GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero) return;

            // Pas réguliers de 4 ms. Timer haute résolution (Windows 10 1803+) : il reste
            // précis sous Windows 11 même sans fenêtre visible, où timeBeginPeriod n'est
            // plus garanti. Repli sur timeBeginPeriod pour les Windows 10 plus anciens.
            IntPtr timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
                NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
            bool highRes = timer != IntPtr.Zero;
            if (!highRes)
            {
                timer = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null, 0, NativeMethods.TIMER_ALL_ACCESS);
                NativeMethods.timeBeginPeriod(1);
            }
            var handles = new[] { timer };
            try
            {
                ApplyCommands();
                while (true)
                {
                    if (_idle)
                    {
                        NativeMethods.MsgWaitForMultipleObjectsEx(0, null, NativeMethods.INFINITE,
                            NativeMethods.QS_ALLINPUT, NativeMethods.MWMO_INPUTAVAILABLE);
                    }
                    else
                    {
                        double waitMs = _nextTickMs - NowMs;
                        if (waitMs > 0)
                        {
                            long due = -(long)(waitMs * 10000); // relatif, en unités de 100 ns
                            if (timer != IntPtr.Zero && NativeMethods.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                                NativeMethods.MsgWaitForMultipleObjectsEx(1, handles, NativeMethods.INFINITE,
                                    NativeMethods.QS_ALLINPUT, NativeMethods.MWMO_INPUTAVAILABLE);
                            else
                                NativeMethods.MsgWaitForMultipleObjectsEx(0, null, (uint)Math.Ceiling(waitMs),
                                    NativeMethods.QS_ALLINPUT, NativeMethods.MWMO_INPUTAVAILABLE);
                        }
                    }

                    // Le hook est appelé par Windows pendant PeekMessage.
                    while (NativeMethods.PeekMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
                    {
                        if (msg.message == NativeMethods.WM_QUIT) return;
                    }

                    ApplyCommands();
                    RunTicks();
                }
            }
            finally
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                if (timer != IntPtr.Zero) NativeMethods.CloseHandle(timer);
                if (!highRes) NativeMethods.timeEndPeriod(1);
            }
        }

        /// <summary>Prend en compte les réglages et le toggle envoyés par l'interface.</summary>
        private void ApplyCommands()
        {
            var cfg = Interlocked.Exchange(ref _pendingConfig, null);
            if (cfg != null)
            {
                _config = cfg;
                _core.Configure(cfg);
            }

            // Réglages neutres (aucune moyenne, vitesse 1) : souris strictement native.
            bool neutral = _config.SmoothingMs < TremorFilter.TickMs && Math.Abs(_config.Speed - 1.0) < 1e-9;
            bool processing = _enabled && !neutral;
            if (processing != _processing)
            {
                _processing = processing;
                _setCursorFailures = 0;
                if (NativeMethods.GetCursorPos(out NativeMethods.POINT p))
                    _core.Anchor(p.X, p.Y);
                else
                    _core.Unanchor();
            }

            if (_recording) WakeUp();
        }

        private void WakeUp()
        {
            if (_idle)
            {
                _idle = false;
                _nextTickMs = NowMs;
                _segment.Clear(); // trou dans le temps : le segment n'est plus continu
            }
        }

        /// <summary>Exécute les pas d'horloge en retard (rattrapage borné), puis s'endort si tout est stable.</summary>
        private void RunTicks()
        {
            if (_idle) return;
            double now = NowMs;
            int steps = 0;
            while (now >= _nextTickMs && steps < 25)
            {
                Tick();
                _nextTickMs += TremorFilter.TickMs;
                steps++;
            }
            if (now >= _nextTickMs) _nextTickMs = now + TremorFilter.TickMs; // thread suspendu longtemps

            if (!_recording && _core.IsIdle(now)) _idle = true;
        }

        private void Tick()
        {
            bool move = _core.Tick(out int x, out int y);
            if (_processing && move && _core.Anchored)
            {
                if (NativeMethods.SetCursorPos(x, y))
                    _setCursorFailures = 0;
                else if (++_setCursorFailures > MaxSetCursorFailures)
                {
                    // Le curseur ne se laisse plus placer (bureau sécurisé…) : on rend la main.
                    _enabled = false;
                    ApplyCommands();
                    AutoDisabled?.Invoke();
                    return;
                }
                // Windows peut borner la position (zone entre deux écrans) : on garde la vraie.
                if (NativeMethods.GetCursorPos(out NativeMethods.POINT p))
                    _core.ConfirmOutput(p.X, p.Y);
            }

            NetSpeed = _core.Filter.NetSpeed;
            ReactiveWeight = _core.Filter.Weight;

            lock (_lock)
            {
                _trace[_traceHead] = new TracePoint
                {
                    HandX = (float)_core.VX, HandY = (float)_core.VY,
                    CursorX = (float)_core.Filter.X, CursorY = (float)_core.Filter.Y
                };
                _traceHead = (_traceHead + 1) % TraceCapacity;
                if (_traceCount < TraceCapacity) _traceCount++;

                if (_recording && _record.Count < RecordCapacity)
                    _record.Add((_core.VX, _core.VY));
            }

            // Lissage coupé = souris souvent prêtée à quelqu'un d'autre : rien n'est mesuré.
            if (_journalOn && _enabled) CollectSegment();
            else _segment.Clear();
        }

        /// <summary>Accumule V ; tous les 3 s continus, analyse le segment (~50 µs).</summary>
        private void CollectSegment()
        {
            _segment.Add((_core.VX, _core.VY));
            if (_segment.Count < SegmentTicks) return;

            var (m, netSpeed) = TremorAnalysis.AnalyzeMotion(_segment, TremorFilter.TickMs);
            _segment.Clear();
            // V est multiplié par la vitesse réglée : on revient aux pixels de la main.
            double speed = Math.Max(0.01, _config.Speed);
            // Main absente, aucune oscillation nette, ou simple bruit d'arrondi : rien à noter.
            if (m.FrequencyHz <= 0 || m.AmplitudePx / speed < TremorJournal.MinAmplitudePx) return;
            lock (_lock)
            {
                if (_journal.Count < JournalCapacity)
                    _journal.Add(new JournalSample
                    {
                        FrequencyHz = m.FrequencyHz,
                        AmplitudePx = m.AmplitudePx / speed,
                        NetSpeedPxS = netSpeed / speed
                    });
            }
        }

        /// <summary>
        /// Callback du hook, appelé pour CHAQUE événement souris du système.
        /// Contrat : rapide, jamais d'exception ; ne bloquer que les déplacements
        /// physiques que l'on remplace nous-mêmes.
        /// </summary>
        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            bool block = false;
            try
            {
                if (nCode >= 0)
                {
                    // MSLLHOOKSTRUCT : pt.x @0, pt.y @4, flags @12. Les événements injectés
                    // (nos SetCursorPos, autres logiciels) passent toujours tels quels.
                    uint flags = (uint)Marshal.ReadInt32(lParam, 12);
                    bool physical = (flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) == 0;
                    int message = wParam.ToInt32();

                    if (physical && message == NativeMethods.WM_MOUSEMOVE)
                    {
                        int x = Marshal.ReadInt32(lParam, 0);
                        int y = Marshal.ReadInt32(lParam, 4);
                        double now = NowMs;

                        if (_processing && NativeMethods.GetCursorPos(out NativeMethods.POINT cur))
                        {
                            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                            int right = left + NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN) - 1;
                            int bottom = top + NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN) - 1;
                            _core.OnMove(x, y, cur.X, cur.Y, now, left, top, right, bottom);
                            block = true;
                        }
                        else
                        {
                            // Souris native : le filtre tourne en simulation pour le visualiseur.
                            _core.OnObservedMove(x, y, now);
                        }
                        WakeUp();
                    }
                    else if (physical && _processing && IsButton(message))
                    {
                        _core.OnButton(NowMs);
                    }
                }
            }
            catch
            {
                // Une exception sortant du hook ferait planter le processus.
                block = false;
            }

            return block ? (IntPtr)1 : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool IsButton(int message) =>
            message == NativeMethods.WM_LBUTTONDOWN || message == NativeMethods.WM_LBUTTONUP ||
            message == NativeMethods.WM_RBUTTONDOWN || message == NativeMethods.WM_RBUTTONUP ||
            message == NativeMethods.WM_MBUTTONDOWN || message == NativeMethods.WM_MBUTTONUP ||
            message == NativeMethods.WM_XBUTTONDOWN || message == NativeMethods.WM_XBUTTONUP;
    }
}
