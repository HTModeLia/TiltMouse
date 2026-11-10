using System;
using System.Collections.Generic;

namespace MouseSmooth
{
    /// <summary>
    /// Auto-test (mode --selftest) : simule une souris et un curseur, sans hook ni
    /// curseur réel, et vérifie le filtre de bout en bout — y compris le calcul du
    /// déplacement par rapport au curseur que l'on a soi-même placé.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failures;

        private static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _failures++;
        }

        /// <summary>
        /// Simulation fidèle au fonctionnement réel : la souris envoie un événement
        /// toutes les 8 ms (125 Hz) avec des déplacements entiers ; Windows propose
        /// pt = curseur + déplacement ; le moteur bloque l'événement et place
        /// lui-même le curseur à chaque pas de 4 ms.
        /// </summary>
        private sealed class Sim
        {
            public readonly MotionCore Core = new MotionCore();
            public int CursorX, CursorY;
            public double TimeMs;
            private int _tick, _lastHandX, _lastHandY;

            public Sim(SmoothConfig cfg, int x = 500, int y = 500)
            {
                Core.Configure(cfg);
                CursorX = x; CursorY = y;
                _lastHandX = x; _lastHandY = y;
            }

            /// <summary>Un pas de 4 ms, la main étant en (handX, handY) en px physiques.</summary>
            public void Step(double handX, double handY)
            {
                TimeMs += TremorFilter.TickMs;
                if (_tick++ % 2 == 0)
                {
                    int hx = (int)Math.Round(handX), hy = (int)Math.Round(handY);
                    int dx = hx - _lastHandX, dy = hy - _lastHandY;
                    _lastHandX = hx; _lastHandY = hy;
                    if (dx != 0 || dy != 0)
                        Core.OnMove(CursorX + dx, CursorY + dy, CursorX, CursorY, TimeMs, 0, 0, 3839, 2159);
                }
                if (Core.Tick(out int x, out int y))
                {
                    CursorX = x; CursorY = y;
                    Core.ConfirmOutput(x, y);
                }
            }

            /// <summary>Recale la main (après un saut externe du curseur, par exemple).</summary>
            public void SetHand(int x, int y) { _lastHandX = x; _lastHandY = y; }
        }

        private static double Sin(double hz, double amplitude, double tMs, double phase = 0) =>
            amplitude * Math.Sin(2 * Math.PI * hz * tMs / 1000.0 + phase);

        private static SmoothConfig Cfg(double ms = 200, double reactivity = 0.5, double speed = 1, double click = 0) =>
            new SmoothConfig { SmoothingMs = ms, Reactivity = reactivity, Speed = speed, ClickStabilizeMs = click };

        public static int Run()
        {
            // 1. Main au repos, tremblement 5 Hz ±8 px sur les deux axes : avec une moyenne
            //    de 200 ms (une période), le curseur doit rester quasi immobile.
            {
                var sim = new Sim(Cfg(200));
                double maxDev = 0;
                for (int i = 0; i < 1500; i++)
                {
                    sim.Step(500 + Sin(5, 8, sim.TimeMs), 500 + Sin(5, 8, sim.TimeMs, 1.3));
                    if (sim.TimeMs > 1000)
                        maxDev = Math.Max(maxDev, Math.Max(Math.Abs(sim.CursorX - 500), Math.Abs(sim.CursorY - 500)));
                }
                Check($"Tremblement 5 Hz ±8 px au repos : curseur stable (écart max {maxDev} px <= 1)", maxDev <= 1);
            }

            // 2. Tremblement à une autre fréquence que celle réglée (4 Hz et 7 Hz mêlés).
            {
                var sim = new Sim(Cfg(200));
                double maxDev = 0;
                for (int i = 0; i < 1500; i++)
                {
                    double t = sim.TimeMs;
                    sim.Step(500 + Sin(4, 6, t) + Sin(7, 4, t, 0.5), 500 + Sin(7, 6, t, 2) + Sin(4, 3, t));
                    if (sim.TimeMs > 1000)
                        maxDev = Math.Max(maxDev, Math.Max(Math.Abs(sim.CursorX - 500), Math.Abs(sim.CursorY - 500)));
                }
                Check($"Tremblement 4 + 7 Hz ±10 px : fortement atténué (écart max {maxDev} px <= 2)", maxDev <= 2);
            }

            // 3. Geste lent (100 px/s vers la droite) avec tremblement vertical 5 Hz ±6 px :
            //    trajectoire droite, sans aller-retour.
            {
                var sim = new Sim(Cfg(200));
                double maxY = 0;
                bool monotonic = true;
                int lastX = sim.CursorX;
                for (int i = 0; i < 750; i++) // 3 s
                {
                    sim.Step(500 + 0.1 * sim.TimeMs, 500 + Sin(5, 6, sim.TimeMs));
                    if (sim.CursorX < lastX) monotonic = false;
                    lastX = sim.CursorX;
                    if (sim.TimeMs > 500) maxY = Math.Max(maxY, Math.Abs(sim.CursorY - 500));
                }
                Check($"Geste lent avec tremblement : ligne droite (écart vertical {maxY} px <= 1, sans recul {monotonic})",
                      maxY <= 1 && monotonic);
            }

            // 4. Arrêt : la main parcourt 300 px puis s'arrête. Le curseur arrive pile
            //    au bon endroit, sans dépasser, peu après l'arrêt.
            {
                var sim = new Sim(Cfg(200));
                int maxX = 0;
                for (int i = 0; i < 250; i++) sim.Step(500 + 300.0 * Math.Min(1, sim.TimeMs / 1000.0), 500);
                for (int i = 0; i < 125; i++) { sim.Step(800, 500); maxX = Math.Max(maxX, sim.CursorX); }
                Check($"Fin de geste : arrivée exacte en 500 ms (x={sim.CursorX}, attendu 800, max {maxX})",
                      sim.CursorX == 800 && sim.CursorY == 500 && maxX <= 800);
            }

            // 5. Réactivité : un grand geste rapide (2000 px/s) suit avec moins de retard
            //    quand la réactivité est active.
            {
                double Lag(double reactivity)
                {
                    var sim = new Sim(Cfg(200, reactivity));
                    for (int i = 0; i < 100; i++) sim.Step(500 + 2.0 * sim.TimeMs, 500); // 0,4 s
                    return 500 + 2.0 * sim.TimeMs - sim.CursorX;
                }
                double slow = Lag(0), fast = Lag(1);
                Check($"Réactivité : retard en grand geste réduit ({fast:0} px contre {slow:0} px)", fast < 0.6 * slow);
            }

            // 6. Vitesse : 100 px de main à vitesse 2 => 200 px de curseur.
            {
                var sim = new Sim(Cfg(200, 0.5, 2.0));
                for (int i = 0; i < 125; i++) sim.Step(500 + Math.Min(100, 0.2 * sim.TimeMs), 500);
                for (int i = 0; i < 150; i++) sim.Step(600, 500);
                Check($"Vitesse ×2 appliquée (x={sim.CursorX}, attendu 700)", sim.CursorX == 700);
            }

            // 7. Stabilisation des clics : la secousse de 10 px juste après l'appui est
            //    ignorée ; un mouvement après le délai passe normalement.
            {
                var sim = new Sim(Cfg(200, 0.5, 1, 150));
                for (int i = 0; i < 10; i++) sim.Step(500, 500);
                sim.Core.OnButton(sim.TimeMs);
                for (int i = 0; i < 10; i++) sim.Step(510, 506);   // secousse pendant 40 ms
                for (int i = 0; i < 150; i++) sim.Step(510, 506);
                bool ignored = sim.CursorX == 500 && sim.CursorY == 500;
                for (int i = 0; i < 25; i++) sim.Step(510 + 2 * i, 506);
                for (int i = 0; i < 150; i++) sim.Step(558, 506);
                Check($"Clic : secousse ignorée ({ignored}), mouvement suivant accepté (x={sim.CursorX}, attendu 548)",
                      ignored && sim.CursorX == 548);
            }

            // 8. Déplacement externe du curseur (autre logiciel) : on repart de là, sans
            //    ramener le curseur à l'ancienne position.
            {
                var sim = new Sim(Cfg(200));
                for (int i = 0; i < 50; i++) sim.Step(500, 500);
                sim.CursorX = 1500; sim.CursorY = 900; // déplacé par un autre programme
                for (int i = 0; i < 150; i++) sim.Step(503, 500);
                Check($"Déplacement externe respecté (x={sim.CursorX},{sim.CursorY}, attendu 1503,900)",
                      sim.CursorX == 1503 && sim.CursorY == 900);
            }

            // 9. Changement de durée en plein geste : aucun saut du curseur.
            {
                var sim = new Sim(Cfg(200));
                int maxStep = 0, prev = sim.CursorX;
                for (int i = 0; i < 300; i++)
                {
                    if (i == 150) sim.Core.Configure(Cfg(400));
                    sim.Step(500 + 0.3 * sim.TimeMs, 500);
                    maxStep = Math.Max(maxStep, Math.Abs(sim.CursorX - prev));
                    prev = sim.CursorX;
                }
                Check($"Changement de réglage en plein geste sans saut (pas max {maxStep} px <= 3)", maxStep <= 3);
            }

            // 10. Calibrage : fréquence et amplitude retrouvées malgré la dérive de la main.
            {
                var rnd = new Random(1);
                foreach (double hz in new[] { 5.0, 8.0 })
                {
                    var samples = new List<(double X, double Y)>();
                    for (int i = 0; i < 1500; i++)
                    {
                        double t = i * TremorFilter.TickMs;
                        samples.Add((Math.Round(300 + 0.01 * t + Sin(hz, 6, t) + rnd.NextDouble() - 0.5),
                                     Math.Round(300 + Sin(hz, 3, t, 1))));
                    }
                    var m = TremorAnalysis.Analyze(samples, TremorFilter.TickMs);
                    double period = 1000 / hz;
                    Check($"Calibrage {hz} Hz : mesuré {m.FrequencyHz:0.00} Hz, ±{m.AmplitudePx:0.0} px, moyenne {m.RecommendedMs} ms",
                          Math.Abs(m.FrequencyHz - hz) < 0.3 && m.AmplitudePx > 5 && m.AmplitudePx < 7.5 &&
                          Math.Abs(m.RecommendedMs - period) <= 8);
                }
            }

            // 11. Journal : tremblement d'action mesuré PENDANT un geste lent (300 px en 3 s,
            // profil naturel « minimum jerk »), puis au repos, puis geste sans tremblement.
            {
                var rnd = new Random(2);
                List<(double X, double Y)> Segment(double travel, double hz, double amp)
                {
                    var s = new List<(double X, double Y)>();
                    for (int i = 0; i < 750; i++)
                    {
                        double t = i * TremorFilter.TickMs, u = i / 749.0;
                        double voluntary = travel * (10 * u * u * u - 15 * u * u * u * u + 6 * u * u * u * u * u);
                        s.Add((Math.Round(400 + voluntary + Sin(hz, amp, t) + rnd.NextDouble() - 0.5),
                               Math.Round(300 + 0.3 * voluntary + Sin(hz, amp / 2, t, 1))));
                    }
                    return s;
                }

                var (g, gSpeed) = TremorAnalysis.AnalyzeMotion(Segment(300, 6, 4), TremorFilter.TickMs);
                Check($"Journal, geste lent + 6 Hz ±4 px : {g.FrequencyHz:0.00} Hz, ±{g.AmplitudePx:0.0} px, {gSpeed:0} px/s (geste)",
                      Math.Abs(g.FrequencyHz - 6) < 0.4 && g.AmplitudePx > 3 && g.AmplitudePx < 5.5 &&
                      gSpeed >= TremorJournal.RestBelowPxS && gSpeed < TremorJournal.GestureBelowPxS);

                var (r, rSpeed) = TremorAnalysis.AnalyzeMotion(Segment(0, 5, 3), TremorFilter.TickMs);
                Check($"Journal, repos + 5 Hz ±3 px : {r.FrequencyHz:0.00} Hz, ±{r.AmplitudePx:0.0} px, {rSpeed:0} px/s (repos)",
                      Math.Abs(r.FrequencyHz - 5) < 0.4 && r.AmplitudePx > 2 && r.AmplitudePx < 4 &&
                      rSpeed < TremorJournal.RestBelowPxS);

                var (n, _) = TremorAnalysis.AnalyzeMotion(Segment(300, 6, 0), TremorFilter.TickMs);
                Check($"Journal, geste sans tremblement : aucun tremblement inventé ({n.FrequencyHz:0.00} Hz, ±{n.AmplitudePx:0.0} px)",
                      n.FrequencyHz == 0 || n.AmplitudePx < TremorJournal.MinAmplitudePx);

                Check("Médiane", TremorJournal.Median(new[] { 3.0, 1, 2 }) == 2 && TremorJournal.Median(new[] { 4.0, 1, 2, 3 }) == 2.5);
            }

            // 12. Mise à jour : lecture du tag GitHub et comparaison de versions.
            Check($"Mise à jour : tags lus, version courante {UpdateCheck.Current}",
                  UpdateCheck.ParseTag("v2.1.0") == new Version(2, 1, 0) &&
                  UpdateCheck.ParseTag("v2.10") > UpdateCheck.ParseTag("v2.9.5") &&
                  UpdateCheck.ParseTag("n'importe quoi") == null &&
                  !(UpdateCheck.ParseTag("v" + UpdateCheck.Current) > UpdateCheck.Current));

            Console.WriteLine(_failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED");
            return _failures == 0 ? 0 : 1;
        }
    }
}
