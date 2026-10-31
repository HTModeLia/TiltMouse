using System;

namespace MouseSmooth
{
    /// <summary>
    /// Logique de déplacement, sans aucun appel système (testable par --selftest) :
    ///  - intègre les déplacements de la main dans la position voulue V ;
    ///  - ignore les mouvements parasites juste après un clic ;
    ///  - détecte un déplacement externe du curseur et se ré-ancre dessus ;
    ///  - à chaque pas d'horloge, calcule le pixel où placer le curseur.
    ///
    /// Point clé : le moteur bloque le mouvement natif, donc le curseur reste là où
    /// on l'a mis. Windows calcule la position proposée (pt) à partir de CETTE
    /// position : le déplacement de la main est pt − position actuelle du curseur
    /// (accélération Windows comprise), jamais pt − pt précédent.
    /// </summary>
    internal sealed class MotionCore
    {
        public readonly TremorFilter Filter = new TremorFilter();

        private SmoothConfig _cfg = new SmoothConfig();
        private double _freezeUntilMs = double.NegativeInfinity;

        /// <summary>Position voulue par la main (px, sous-pixel conservé).</summary>
        public double VX { get; private set; }
        public double VY { get; private set; }

        /// <summary>Pixel où le curseur a été placé en dernier.</summary>
        public int OutX { get; private set; }
        public int OutY { get; private set; }

        public bool Anchored { get; private set; }

        /// <summary>Horodatage du dernier mouvement physique reçu (ms).</summary>
        public double LastInputMs { get; private set; } = double.NegativeInfinity;

        public void Configure(SmoothConfig cfg)
        {
            _cfg = cfg;
            Filter.Configure(cfg);
        }

        /// <summary>Ré-ancre tout sur une position réelle du curseur (aucun saut).</summary>
        public void Anchor(int x, int y)
        {
            VX = x; VY = y;
            OutX = x; OutY = y;
            Filter.Reset(x, y);
            Anchored = true;
        }

        /// <summary>Oublie l'ancrage : le prochain mouvement repartira de la position réelle.</summary>
        public void Unanchor() => Anchored = false;

        /// <summary>
        /// Mouvement physique pendant le lissage. (ptX, ptY) : position proposée par
        /// Windows ; (curX, curY) : position actuelle du curseur ; bornes : bureau
        /// virtuel (tous écrans), V n'en sort jamais.
        /// </summary>
        public void OnMove(int ptX, int ptY, int curX, int curY, double nowMs,
                           int left, int top, int right, int bottom)
        {
            // Premier mouvement, ou le curseur a été déplacé par quelqu'un d'autre
            // (autre logiciel, tactile, fenêtre administrateur…) : on repart de là.
            if (!Anchored || curX != OutX || curY != OutY)
                Anchor(curX, curY);

            LastInputMs = nowMs;
            if (nowMs < _freezeUntilMs) return; // secousse due au clic : ignorée

            VX = Math.Min(right, Math.Max(left, VX + (ptX - curX) * _cfg.Speed));
            VY = Math.Min(bottom, Math.Max(top, VY + (ptY - curY) * _cfg.Speed));
        }

        /// <summary>Mouvement observé sans lissage (souris native) : V suit le curseur réel.</summary>
        public void OnObservedMove(int ptX, int ptY, double nowMs)
        {
            if (!Anchored) Anchor(ptX, ptY);
            VX = ptX; VY = ptY;
            OutX = ptX; OutY = ptY;
            LastInputMs = nowMs;
        }

        /// <summary>Appui ou relâchement d'un bouton : gel des mouvements de la main.</summary>
        public void OnButton(double nowMs)
        {
            if (_cfg.ClickStabilizeMs > 0)
                _freezeUntilMs = nowMs + _cfg.ClickStabilizeMs;
        }

        /// <summary>
        /// Un pas d'horloge (<see cref="TremorFilter.TickMs"/>). Renvoie true si le
        /// curseur doit être déplacé vers (x, y).
        /// </summary>
        public bool Tick(out int x, out int y)
        {
            Filter.Push(VX, VY);
            if (Filter.IsSettled(VX, VY))
            {
                // Main arrêtée et moyenne convergée : arrondi exact, le curseur
                // termine pile sur le pixel voulu.
                x = (int)Math.Round(Filter.X);
                y = (int)Math.Round(Filter.Y);
            }
            else
            {
                x = TremorFilter.Quantize(OutX, Filter.X);
                y = TremorFilter.Quantize(OutY, Filter.Y);
            }
            return x != OutX || y != OutY;
        }

        /// <summary>Position réellement obtenue après SetCursorPos (Windows peut la borner).</summary>
        public void ConfirmOutput(int x, int y)
        {
            OutX = x;
            OutY = y;
        }

        /// <summary>Plus rien à animer : la sortie a rejoint V et la main est immobile.</summary>
        public bool IsIdle(double nowMs) =>
            nowMs - LastInputMs > 2 * Filter.WindowTicks * TremorFilter.TickMs + 50 &&
            Filter.IsSettled(VX, VY);
    }
}
