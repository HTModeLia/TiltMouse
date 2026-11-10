using System;
using System.Collections.Generic;

namespace MouseSmooth
{
    /// <summary>Résultat d'une mesure de tremblement.</summary>
    internal readonly struct TremorMeasure
    {
        public TremorMeasure(double amplitudePx, double frequencyHz, double recommendedMs)
        {
            AmplitudePx = amplitudePx;
            FrequencyHz = frequencyHz;
            RecommendedMs = recommendedMs;
        }

        /// <summary>Amplitude du tremblement (px, demi-crête sur l'axe le plus agité).</summary>
        public double AmplitudePx { get; }

        /// <summary>Fréquence dominante (Hz), 0 si aucune périodicité nette.</summary>
        public double FrequencyHz { get; }

        /// <summary>Durée de moyenne recommandée (ms).</summary>
        public double RecommendedMs { get; }
    }

    /// <summary>
    /// Analyse d'un enregistrement de la main au repos, échantillonné à cadence fixe :
    /// amplitude et fréquence du tremblement, d'où la durée de moyenne idéale (une
    /// période : la moyenne glissante annule alors exactement le tremblement).
    /// </summary>
    internal static class TremorAnalysis
    {
        public const double MinHz = 2.5, MaxHz = 15.0;

        public static TremorMeasure Analyze(IReadOnlyList<(double X, double Y)> samples, double dtMs)
        {
            int n = samples.Count;
            if (n < 32) return new TremorMeasure(0, 0, 0);

            // Dérive lente de la main retirée (régression linéaire par axe) : seule
            // l'oscillation autour de cette droite est du tremblement.
            double[] x = Detrend(samples, p => p.X);
            double[] y = Detrend(samples, p => p.Y);

            double rmsX = Rms(x), rmsY = Rms(y);
            double amplitude = Math.Sqrt(2) * Math.Max(rmsX, rmsY); // sinusoïde : A = √2 × RMS

            // Autocorrélation normalisée (axes cumulés) sur les périodes plausibles.
            int minLag = Math.Max(1, (int)Math.Floor(1000.0 / MaxHz / dtMs));
            int maxLag = Math.Min(n / 2, (int)Math.Ceiling(1000.0 / MinHz / dtMs));
            double r0 = 0;
            for (int i = 0; i < n; i++) r0 += x[i] * x[i] + y[i] * y[i];

            double frequency = 0;
            if (r0 > 1e-9 && maxLag > minLag + 2)
            {
                var r = new double[maxLag + 2];
                double best = 0;
                for (int lag = minLag - 1; lag <= maxLag + 1 && lag < n; lag++)
                {
                    if (lag < 1) continue;
                    double s = 0;
                    for (int i = 0; i + lag < n; i++) s += x[i] * x[i + lag] + y[i] * y[i + lag];
                    r[lag] = s / r0 * n / (n - lag); // correction du recouvrement décroissant
                    if (lag >= minLag && lag <= maxLag && r[lag] > best) best = r[lag];
                }

                // Premier pic marqué (évite de prendre un multiple de la période).
                if (best > 0.3)
                {
                    for (int lag = Math.Max(minLag, 2); lag <= maxLag; lag++)
                    {
                        if (r[lag] >= 0.8 * best && r[lag] >= r[lag - 1] && r[lag] >= r[lag + 1])
                        {
                            // Interpolation parabolique : période sous l'échantillon.
                            double a = r[lag - 1], b = r[lag], c = r[lag + 1];
                            double den = a - 2 * b + c;
                            double offset = Math.Abs(den) > 1e-12 ? 0.5 * (a - c) / den : 0;
                            frequency = 1000.0 / ((lag + offset) * dtMs);
                            break;
                        }
                    }
                }
            }

            double recommended = frequency > 0 ? 1000.0 / frequency : 200.0;
            recommended = Math.Min(400, Math.Max(100, Math.Round(recommended / 4.0) * 4.0));
            return new TremorMeasure(amplitude, frequency, recommended);
        }

        /// <summary>
        /// Segment enregistré pendant l'usage normal (journal) : le geste volontaire,
        /// lent, est retiré par un passe-haut (V moins sa moyenne triangulaire centrée
        /// de ~0,8 s, nulle à 2,5 Hz et au-delà) ; ce qui reste est analysé comme au
        /// calibrage. Renvoie aussi la vitesse nette du segment (px/s) pour classer
        /// repos / geste.
        /// </summary>
        public static (TremorMeasure Measure, double NetSpeedPxS) AnalyzeMotion(
            IReadOnlyList<(double X, double Y)> samples, double dtMs)
        {
            int n = samples.Count;
            int half = (int)Math.Round(200 / dtMs);          // boîte de ~400 ms (impaire)
            if (n < 4 * half + 32) return (new TremorMeasure(0, 0, 0), 0);

            double dx = samples[n - 1].X - samples[0].X, dy = samples[n - 1].Y - samples[0].Y;
            double netSpeed = Math.Sqrt(dx * dx + dy * dy) / ((n - 1) * dtMs / 1000.0);

            double[] x = HighPass(samples, p => p.X, half);
            double[] y = HighPass(samples, p => p.Y, half);
            var residual = new List<(double X, double Y)>(x.Length);
            for (int i = 0; i < x.Length; i++) residual.Add((x[i], y[i]));
            return (Analyze(residual, dtMs), netSpeed);
        }

        /// <summary>V moins deux moyennes glissantes centrées en cascade ; bords (2 × half) retirés.</summary>
        private static double[] HighPass(IReadOnlyList<(double X, double Y)> samples,
                                         Func<(double X, double Y), double> axis, int half)
        {
            int n = samples.Count;
            var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = axis(samples[i]);
            double[] once = Box(v, half), twice = Box(once, half);

            var res = new double[n - 4 * half];
            for (int i = 0; i < res.Length; i++) res[i] = v[i + 2 * half] - twice[i + 2 * half];
            return res;
        }

        private static double[] Box(double[] v, int half)
        {
            var prefix = new double[v.Length + 1];
            for (int i = 0; i < v.Length; i++) prefix[i + 1] = prefix[i] + v[i];
            var r = new double[v.Length];
            for (int i = half; i < v.Length - half; i++)
                r[i] = (prefix[i + half + 1] - prefix[i - half]) / (2 * half + 1);
            return r;
        }

        private static double[] Detrend(IReadOnlyList<(double X, double Y)> samples, Func<(double X, double Y), double> axis)
        {
            int n = samples.Count;
            double sumT = 0, sumV = 0, sumTT = 0, sumTV = 0;
            for (int i = 0; i < n; i++)
            {
                double v = axis(samples[i]);
                sumT += i; sumV += v; sumTT += (double)i * i; sumTV += i * v;
            }
            double den = n * sumTT - sumT * sumT;
            double slope = den != 0 ? (n * sumTV - sumT * sumV) / den : 0;
            double intercept = (sumV - slope * sumT) / n;

            var res = new double[n];
            for (int i = 0; i < n; i++) res[i] = axis(samples[i]) - (slope * i + intercept);
            return res;
        }

        private static double Rms(double[] v)
        {
            double s = 0;
            foreach (double d in v) s += d * d;
            return Math.Sqrt(s / v.Length);
        }
    }
}
