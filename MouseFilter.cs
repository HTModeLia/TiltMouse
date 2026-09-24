using System;

namespace MouseSmooth
{
    /// <summary>État interne du filtre (manipulé uniquement sur le thread UI).</summary>
    internal struct FilterState
    {
        public bool HasPosition;
        public float RawX, RawY;   // dernière position matérielle brute
        public float VX, VY;       // position "virtuelle" : intégration des deltas bruts × gain
        public float CX, CY;       // position "engagée" : poursuite filtrée de V, gelée sous le seuil
        public float GateX, GateY; // ouverture du seuil par axe (0 = gelé, 1 = suivi complet)
        public float OutX, OutY;   // dernière position appliquée au curseur
        public float EX, EY;       // résidu en attente (bande morte)
        public uint LastTime;      // horodatage système du dernier événement (ms)

        public static FilterState Anchored(int x, int y, uint time)
        {
            var s = default(FilterState);
            s.HasPosition = true;
            s.RawX = x; s.RawY = y;
            s.VX = x; s.VY = y;
            s.CX = x; s.CY = y;
            s.OutX = x; s.OutY = y;
            s.LastTime = time;
            return s;
        }
    }

    /// <summary>
    /// Filtre anti-tremblement :
    ///  1. les deltas bruts sont intégrés avec la sensibilité par axe dans une position
    ///     virtuelle V (boucle ouverte : gain 2 =&gt; distance réellement doublée). L'intégration
    ///     est télescopique (V = ancre + gain × déplacement total) : aucune dérive cumulée ;
    ///  2. SEUIL DE TREMBLEMENT : la position ENGAGÉE C ne poursuit V que lorsque l'écart
    ///     |V−C| dépasse le seuil (porte douce sur la moitié du seuil). Le discriminant est
    ///     le DÉPLACEMENT résiduel, pas la vitesse : un tremblement (oscillation sur place,
    ///     même rapide) maintient |V−C| sous le seuil => C reste strictement gelée et le
    ///     curseur ne bouge pas ; une dérive ou un geste fait croître l'écart sans retour
    ///     => la porte s'ouvre et C rattrape V progressivement (aucun saut) ;
    ///  3. au-delà du seuil, la cible mélange position engagée (filtrée) et position
    ///     virtuelle (native) selon la force de lissage. Le suivi est adaptatif : la main
    ///     rapide atteint exactement sa cible d'origine — le lissage n'introduit qu'un
    ///     léger retard, jamais une perte de distance. Gain pointeur, DPI et accélération
    ///     Windows restent inchangés ;
    ///  4. une bande morte (px) évite d'appliquer les micro-corrections résiduelles.
    /// Le repositionnement est absolu (SetCursorPos) : aucune dérive cumulée. Un saut
    /// externe (autre application, reprise de session) ou un trou temporel ré-ancre le
    /// filtre ; la porte, initialement fermée, ne s'ouvre qu'au-delà du seuil — ce qui
    /// fournit naturellement une reprise progressive après ré-ancrage.
    /// </summary>
    internal static class MouseFilter
    {
        public const float SnapDistance = 250f;  // saut brut (px/événement) => resynchronisation
        public const float TauMs = 250f;         // constante de temps de la poursuite C -> V
        public const float GateTauMs = 120f;     // lissage temporel de la porte (anti à-coups)
        public const uint MaxGapMs = 1000;       // trou temporel => resynchronisation

        /// <summary>
        /// Traitement d'un déplacement brut. Renvoie true si la cible doit être
        /// appliquée au curseur via SetCursorPos (targetX/targetY).
        /// </summary>
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
                s.CX = rx; s.CY = ry;
                s.OutX = rx; s.OutY = ry;
                s.EX = 0; s.EY = 0;
                s.GateX = 0; s.GateY = 0;   // porte fermée : reprise progressive après ré-ancrage
                s.LastTime = time;
                targetX = rx; targetY = ry;
                return true;
            }

            // 1. Sensibilité par axe sur les deltas bruts (boucle ouverte).
            s.VX += (rx - s.RawX) * (float)cfg.GainX;
            s.VY += (ry - s.RawY) * (float)cfg.GainY;
            s.RawX = rx; s.RawY = ry;

            if (dt < 1) dt = 1;
            float lambda = dt / (TauMs + dt);

            // 2. Seuil de tremblement, par axe : la porte s'ouvre quand l'écart V−C
            //    (déplacement qui n'a pas été "validé") dépasse le seuil, et se referme
            //    sous la moitié du seuil (hystérésis spatiale : pas de clignotement à la
            //    frontière). Porte fermée => C gelée, curseur immobile. La porte est
            //    ensuite lissée dans le temps pour que les transitions soient douces.
            float threshold = (float)Math.Max(0.0, cfg.TremorThreshold);
            float gateLambda = dt / (GateTauMs + dt);
            s.GateX += (GateValue(MathF.Abs(s.VX - s.CX), threshold) - s.GateX) * gateLambda;
            s.GateY += (GateValue(MathF.Abs(s.VY - s.CY), threshold) - s.GateY) * gateLambda;

            // Poursuite filtrée : C ne rattrape V qu'à hauteur de l'ouverture de la porte.
            // (gelée quand la porte est fermée, rattrapage progressif puis suivi sinon).
            s.CX += (s.VX - s.CX) * (lambda * s.GateX);
            s.CY += (s.VY - s.CY) * (lambda * s.GateY);

            // 3. Cible : engagée (filtrée) + fraction de l'écart restant, proportionnelle
            //    à l'ouverture de la porte ET à la force de lissage choisie.
            float baseMix = 1f - (float)cfg.Smoothing;
            float tx = s.CX + (s.VX - s.CX) * (s.GateX * baseMix);
            float ty = s.CY + (s.VY - s.CY) * (s.GateY * baseMix);

            // 4. Bande morte : on n'applique que les déplacements significatifs. Elle est
            //    modulée par l'ouverture de la porte : porte (quasi) fermée, la cible est
            //    stationnaire et une bande morte fixe créerait un cycle limite (le curseur
            //    oscillerait ±db autour de la cible au lieu de rester immobile) — Out est
            //    donc collé à la cible. Porte ouverte : bande morte nominale.
            float dbX = (float)cfg.DeadbandX * s.GateX;
            float dbY = (float)cfg.DeadbandY * s.GateY;
            s.EX += tx - s.OutX;
            s.EY += ty - s.OutY;

            if (MathF.Abs(s.EX) >= dbX) { s.OutX += s.EX; s.EX = 0; moved = true; }
            if (MathF.Abs(s.EY) >= dbY) { s.OutY += s.EY; s.EY = 0; moved = true; }

            s.LastTime = time;
            targetX = (int)MathF.Round(s.OutX);
            targetY = (int)MathF.Round(s.OutY);
            return moved;
        }

        /// <summary>
        /// Ouverture de la porte (0..1) pour un écart donné (px) :
        /// fermée sous seuil/2, ouverture linéaire jusqu'au seuil, pleine au-delà.
        /// </summary>
        private static float GateValue(float gap, float threshold)
        {
            if (threshold <= 0f) return 1f;             // seuil désactivé
            float half = threshold * 0.5f;
            return gap <= half ? 0f : MathF.Min(1f, (gap - half) / half);
        }
    }
}
