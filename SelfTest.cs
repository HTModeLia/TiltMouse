using System;

namespace MouseSmooth
{
    /// <summary>
    /// Auto-test du filtre (mode --selftest, sans hook ni curseur réel) :
    /// tremblement neutralisé, geste suivi, gains X/Y, ré-ancrage sur saut externe.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failures;

        private static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _failures++;
        }

        public static int Run()
        {
            // 1. Tremblement en place (+/- 6 px, 125 Hz) : le curseur doit rester quasi immobile.
            {
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1 };
                var s = FilterState.Anchored(500, 500, 0);
                double maxDev = 0, finalDev = 0;
                uint t = 0;
                var rnd = new Random(1234);
                for (int i = 0; i < 2000; i++)
                {
                    t += 8;
                    int jx = rnd.Next(-6, 7);
                    int jy = rnd.Next(-6, 7);
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

            // 2. Trajectoire lente et continue : le curseur suit, avec un retard borné, jamais en avance.
            {
                var cfg = new SmoothConfig { Smoothing = 0.6, DeadbandX = 2, DeadbandY = 2, GainX = 1, GainY = 1 };
                var s = FilterState.Anchored(500, 500, 0);
                uint t = 0;
                bool ok = true;
                int lastTx = 500;
                for (int i = 1; i <= 3000; i++)
                {
                    t += 8;
                    MouseFilter.Step(ref s, cfg, 500 + i, 500, t, out int tx, out int ty);
                    if (tx > 500 + i) ok = false;          // jamais en avance sur la consigne
                    if (tx < lastTx) ok = false;           // jamais de marche arrière
                    lastTx = tx;
                    if (ty != 500) ok = false;
                }
                // Retard de lissage attendu ~19 px en fin de rampe (Tau=250 ms, mix 0,4).
                Check($"Geste lent suivi (tx final {lastTx}, attendu 3470..3495)", ok && lastTx >= 3470 && lastTx <= 3495);
            }

            // 3. Sensibilité X != Y : un déplacement (100, 50) doit donner (200, 50) avec (2.0, 1.0).
            {
                var cfg = new SmoothConfig { Smoothing = 0.0, DeadbandX = 0, DeadbandY = 0, GainX = 2.0, GainY = 1.0 };
                var s = FilterState.Anchored(500, 500, 0);
                int tx = 0, ty = 0;
                for (int i = 1; i <= 100; i++)
                    MouseFilter.Step(ref s, cfg, 500 + i, 500 + i / 2, (uint)(i * 8), out tx, out ty);
                Check($"Gain X appliqué (tx={tx}, attendu 700)", Math.Abs(tx - 700) <= 2);
                Check($"Gain Y appliqué (ty={ty}, attendu 550)", Math.Abs(ty - 550) <= 2);
            }

            // 4. Saut externe : ré-ancre immédiatement, même avec lissage maximal.
            {
                var cfg = new SmoothConfig { Smoothing = 0.95, DeadbandX = 20, DeadbandY = 20, GainX = 1, GainY = 1 };
                var s = FilterState.Anchored(500, 500, 0);
                MouseFilter.Step(ref s, cfg, 520, 500, 8, out int tx1, out _);   // geste continu -> filtré
                MouseFilter.Step(ref s, cfg, 1900, 500, 16, out int tx2, out _); // saut externe > 250 px
                Check($"Geste continu filtré (tx1={tx1} < 520)", tx1 < 520);
                Check($"Saut externe ré-ancré (tx2={tx2})", tx2 == 1900);
            }

            // 5. Reprise après trou temporel (> 1 s) : pas de saut résiduel.
            {
                var cfg = new SmoothConfig { Smoothing = 0.8, DeadbandX = 3, DeadbandY = 3, GainX = 1, GainY = 1 };
                var s = FilterState.Anchored(500, 500, 0);
                MouseFilter.Step(ref s, cfg, 505, 500, 8, out _, out _);
                MouseFilter.Step(ref s, cfg, 560, 500, 2500, out int tx, out _); // trou de 2,5 s
                Check($"Trou temporel ré-ancré (tx={tx})", tx == 560);
            }

            Console.WriteLine(_failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED");
            return _failures == 0 ? 0 : 1;
        }
    }
}
