using System;

namespace MouseSmooth
{
    /// <summary>
    /// Auto-test du filtre (mode --selftest, sans hook ni curseur réel) :
    /// tremblement neutralisé, geste suivi, gains X/Y, seuil de tremblement
    /// (verrouillage / déverrouillage), ré-ancrage sur saut externe.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failures;

        private static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _failures++;
        }

        /// <summary>Tremblement sinusoïdal moyen nul (modèle simple de main qui tremble).</summary>
        private static int Tremor(double tHz, double amplitude, int i)
        {
            return (int)Math.Round(amplitude * Math.Sin(2 * Math.PI * tHz * i / 125.0));
        }

        public static int Run()
        {
            // 1. Tremblement en place (±6 px, 2,5 Hz effectif, 125 Hz) : le curseur
            //    doit rester quasi immobile (seuil 30 px/s, lissage 0,8).
            {
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 30 };
                var s = FilterState.Anchored(500, 500, 0);
                double maxDev = 0, finalDev = 0;
                uint t = 0;
                for (int i = 0; i < 2000; i++)
                {
                    t += 8;
                    int jx = Tremor(2.5, 6, i), jy = Tremor(3.1, 6, i);
                    if (MouseFilter.Step(ref s, cfg, 500 + jx, 500 + jy, t, out int tx, out int ty))
                    {
                        double dev = Math.Max(Math.Abs(tx - 500), Math.Abs(ty - 500));
                        if (dev > maxDev) maxDev = dev;
                        finalDev = dev;
                    }
                }
                Check($"Tremblement neutralisé (écart max {maxDev:0.0} px <= 8, final {finalDev:0.0} <= 4)",
                      maxDev <= 8 && finalDev <= 4);
            }

            // 2. Trajectoire lente et continue : le curseur suit, jamais en avance,
            //    jamais de marche arrière ; la distance d'origine est préservée.
            {
                var cfg = new SmoothConfig { Smoothing = 0.6, DeadbandX = 2, DeadbandY = 2, GainX = 1, GainY = 1, TremorThreshold = 30 };
                var s = FilterState.Anchored(500, 500, 0);
                uint t = 0;
                bool ok = true;
                int lastTx = 500;
                for (int i = 1; i <= 3000; i++)
                {
                    t += 8;
                    MouseFilter.Step(ref s, cfg, 500 + i, 500, t, out int tx, out int ty);
                    if (tx > 500 + i) ok = false;   // jamais en avance sur la consigne
                    if (tx < lastTx) ok = false;    // jamais de marche arrière
                    lastTx = tx;
                    if (ty != 500) ok = false;
                }
                // Retard de lissage attendu (Tau=250 ms, mix adaptatif ~0,55 en fin de rampe).
                Check($"Geste lent suivi (tx final {lastTx}, attendu 3450..3500)", ok && lastTx >= 3450 && lastTx <= 3500);
            }

            // 3. Sensibilité X != Y : un déplacement (100, 50) doit donner (200, 50) avec (2.0, 1.0).
            {
                var cfg = new SmoothConfig { Smoothing = 0.0, DeadbandX = 0, DeadbandY = 0, GainX = 2.0, GainY = 1.0, TremorThreshold = 0 };
                var s = FilterState.Anchored(500, 500, 0);
                int tx = 0, ty = 0;
                for (int i = 1; i <= 100; i++)
                    MouseFilter.Step(ref s, cfg, 500 + i, 500 + i / 2, (uint)(i * 8), out tx, out ty);
                Check($"Gain X appliqué (tx={tx}, attendu 700)", Math.Abs(tx - 700) <= 2);
                Check($"Gain Y appliqué (ty={ty}, attendu 550)", Math.Abs(ty - 550) <= 2);
            }

            // 4. Seuil de tremblement : un tremblement DOIT être gelé avec un seuil haut,
            //    et un vrai geste (dérive soutenue) DOIT débloquer dès qu'il dépasse le seuil.
            {
                // 4a. Même tremblement qu'au test 1, mais seuil 200 px/s : vitesse
                //     soutenue quasi nulle => curseur figé (déplacement net ~ 0).
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 200 };
                var s = FilterState.Anchored(500, 500, 0);
                uint t = 0;
                int maxNet = 0;
                for (int i = 0; i < 2000; i++)
                {
                    t += 8;
                    int jx = Tremor(2.5, 6, i), jy = Tremor(3.1, 6, i);
                    if (MouseFilter.Step(ref s, cfg, 500 + jx, 500 + jy, t, out int tx, out int ty))
                        maxNet = Math.Max(maxNet, Math.Max(Math.Abs(tx - 500), Math.Abs(ty - 500)));
                }
                Check($"Seuil haut : tremblement figé (écart max {maxNet} px <= 3)", maxNet <= 3);

                // 4b. Dérive lente soutenue (31 px/s, juste au-dessus du seuil 30) :
                //     le curseur doit avancer (comportement natif sous le seuil figé).
                var cfgOn = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 30 };
                var sOn = FilterState.Anchored(500, 500, 0);
                t = 0;
                int reached = 500;
                for (int i = 0; i < 375; i++) // 3 s à 125 Hz
                {
                    t += 8;
                    if (MouseFilter.Step(ref sOn, cfgOn, 500 + i / 4, 500, t, out int tx, out _))
                        reached = Math.Max(reached, tx);
                }
                Check($"Geste au-dessus du seuil débloqué (avance {reached - 500} px >= 40)", reached - 500 >= 40);

                // 4c. Même dérive, seuil 200 px/s : sous le seuil => quasi figé.
                var cfgOff = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 200 };
                var sOff = FilterState.Anchored(500, 500, 0);
                t = 0;
                int drifted = 0;
                for (int i = 0; i < 375; i++)
                {
                    t += 8;
                    if (MouseFilter.Step(ref sOff, cfgOff, 500 + i / 4, 500, t, out int tx, out _))
                        drifted = Math.Max(drifted, Math.Abs(tx - 500));
                }
                Check($"Geste sous le seuil maintenu filtré (dérive {drifted} px <= 12)", drifted <= 12);
            }

            // 5. Saut externe : ré-ancre immédiatement, même avec lissage maximal.
            {
                var cfg = new SmoothConfig { Smoothing = 0.95, DeadbandX = 20, DeadbandY = 20, GainX = 1, GainY = 1, TremorThreshold = 30 };
                var s = FilterState.Anchored(500, 500, 0);
                MouseFilter.Step(ref s, cfg, 520, 500, 8, out int tx1, out _);   // geste continu -> filtré
                MouseFilter.Step(ref s, cfg, 1900, 500, 16, out int tx2, out _); // saut externe > 250 px
                Check($"Geste continu filtré (tx1={tx1} < 520)", tx1 < 520);
                Check($"Saut externe ré-ancré (tx2={tx2})", tx2 == 1900);
            }

            // 6. Reprise après trou temporel (> 1 s) : pas de saut résiduel.
            {
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 30 };
                var s = FilterState.Anchored(500, 500, 0);
                MouseFilter.Step(ref s, cfg, 505, 500, 8, out _, out _);
                MouseFilter.Step(ref s, cfg, 560, 500, 2500, out int tx, out _); // trou de 2,5 s
                Check($"Trou temporel ré-ancré (tx={tx})", tx == 560);
            }

            // 7. Soupape anti-blocage : même avec un seuil très haut, une dérive
            //    soutenue finit par ouvrir le suivi en force (à 2× le seuil d'écart).
            {
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1, TremorThreshold = 100 };
                var s = FilterState.Anchored(500, 500, 0);
                uint t = 0;
                int reached = 500;
                for (int i = 0; i < 1250; i++) // 10 s à 125 Hz, dérive 60 px/s
                {
                    t += 8;
                    if (MouseFilter.Step(ref s, cfg, 500 + i * 3 / 5, 500, t, out int tx, out _))
                        reached = Math.Max(reached, tx);
                }
                Check($"Soupape anti-blocage : dérive soutenue finit par passer (avance {reached - 500} px >= 150)",
                      reached - 500 >= 150);
            }

            Console.WriteLine(_failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED");
            return _failures == 0 ? 0 : 1;
        }
    }
}
