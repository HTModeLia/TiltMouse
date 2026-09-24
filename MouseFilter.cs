using System;

namespace MouseSmooth
{
    /// <summary>État interne du filtre (manipulé uniquement sur le thread UI).</summary>
    internal struct FilterState
    {
        public bool HasPosition;
        public float RawX, RawY;     // dernière position matérielle brute
        public float VX, VY;         // position "virtuelle" : intégration des deltas bruts × gain
        public float SlowX, SlowY;   // suivi lent (passe-bas) de V : mouvement volontaire
        public float OutX, OutY;     // dernière position appliquée au curseur
        public float EX, EY;         // résidu en attente (bande morte)
        public uint LastTime;        // horodatage système du dernier événement (ms)

        public static FilterState Anchored(int x, int y, uint time)
        {
            var s = default(FilterState);
            s.HasPosition = true;
            s.RawX = x; s.RawY = y;
            s.VX = x; s.VY = y;
            s.SlowX = x; s.SlowY = y;
            s.OutX = x; s.OutY = y;
            s.LastTime = time;
            return s;
        }
    }

    /// <summary>
    /// Filtre anti-tremblement :
    ///  1. les deltas bruts sont intégrés avec la sensibilité par axe dans une position
    ///     virtuelle V (boucle ouverte : gain 2 =&gt; distance réellement doublée) ;
    ///  2. un suivi lent (passe-bas, constante de temps TauMs, horodatage système) extrait
    ///     le mouvement volontaire du tremblement ;
    ///  3. la cible mélange composante lente et rapide selon la force de lissage ;
    ///  4. une bande morte (px) évite d'appliquer les micro-corrections résiduelles.
    /// Le repositionnement est absolu (SetCursorPos) : aucune dérive cumulée. Un saut
    /// externe (autre application, reprise de session) ré-ancre le filtre sans filtrer.
    /// </summary>
    internal static class MouseFilter
    {
        public const float SnapDistance = 250f;  // saut brut (px/événement) => resynchronisation
        public const float TauMs = 250f;         // constante de temps du suivi lent
        public const uint MaxGapMs = 1000;       // trou temporel => resynchronisation

        public static bool Step(ref FilterState s, SmoothConfig cfg, int rx, int ry, uint time,
                                out int targetX, out int targetY)
        {
            bool moved = false;
            uint dt = time - s.LastTime;

            // Saut externe ou trou temporel : on ré-ancre tout sur la position brute,
            // sans filtrer (comportement natif pour ce déplacement-là).
            if (!s.HasPosition || dt > MaxGapMs ||
                MathF.Abs(rx - s.RawX) > SnapDistance || MathF.Abs(ry - s.RawY) > SnapDistance)
            {
                s.HasPosition = true;
                s.RawX = rx; s.RawY = ry;
                s.VX = rx; s.VY = ry;
                s.SlowX = rx; s.SlowY = ry;
                s.OutX = rx; s.OutY = ry;
                s.EX = 0; s.EY = 0;
                s.LastTime = time;
                targetX = rx; targetY = ry;
                return true;
            }

            // 1. Sensibilité par axe sur les deltas bruts (boucle ouverte).
            s.VX += (rx - s.RawX) * (float)cfg.GainX;
            s.VY += (ry - s.RawY) * (float)cfg.GainY;
            s.RawX = rx; s.RawY = ry;

            if (dt < 1) dt = 1;

            // 2. Suivi lent : isole la composante volontaire du mouvement.
            float lambda = dt / (TauMs + dt);
            s.SlowX += (s.VX - s.SlowX) * lambda;
            s.SlowY += (s.VY - s.SlowY) * lambda;

            // 3. Cible : mélange lent/rapide. mix = 1 => aucun lissage.
            float mix = 1f - (float)cfg.Smoothing;
            float tx = s.SlowX + (s.VX - s.SlowX) * mix;
            float ty = s.SlowY + (s.VY - s.SlowY) * mix;

            // 4. Bande morte : on n'applique que les déplacements significatifs.
            s.EX += tx - s.OutX;
            s.EY += ty - s.OutY;

            if (MathF.Abs(s.EX) >= (float)cfg.DeadbandX) { s.OutX += s.EX; s.EX = 0; moved = true; }
            if (MathF.Abs(s.EY) >= (float)cfg.DeadbandY) { s.OutY += s.EY; s.EY = 0; moved = true; }

            s.LastTime = time;
            targetX = (int)MathF.Round(s.OutX);
            targetY = (int)MathF.Round(s.OutY);
            return moved;
        }
    }
}
