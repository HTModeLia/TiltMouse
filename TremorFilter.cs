using System;

namespace MouseSmooth
{
    /// <summary>
    /// Moyenne glissante 2D (fenêtre de N échantillons) à somme courante.
    /// La somme est recalculée exactement de temps en temps : aucune dérive
    /// numérique, même après des heures de fonctionnement.
    /// </summary>
    internal sealed class MovingAverage
    {
        private double[] _bx = new double[1], _by = new double[1];
        private int _head, _sinceRecompute;
        private double _sx, _sy;

        public int Length => _bx.Length;
        public double X { get; private set; }
        public double Y { get; private set; }

        /// <summary>Change la longueur de fenêtre et la remplit avec (x, y) : sortie = (x, y).</summary>
        public void Reset(int length, double x, double y)
        {
            length = Math.Max(1, length);
            if (_bx.Length != length)
            {
                _bx = new double[length];
                _by = new double[length];
            }
            for (int i = 0; i < length; i++) { _bx[i] = x; _by[i] = y; }
            _sx = x * length;
            _sy = y * length;
            _head = 0;
            _sinceRecompute = 0;
            X = x;
            Y = y;
        }

        public void Push(double x, double y)
        {
            _sx += x - _bx[_head];
            _sy += y - _by[_head];
            _bx[_head] = x;
            _by[_head] = y;
            if (++_head == _bx.Length) _head = 0;

            if (++_sinceRecompute >= 4096)
            {
                _sinceRecompute = 0;
                _sx = 0; _sy = 0;
                for (int i = 0; i < _bx.Length; i++) { _sx += _bx[i]; _sy += _by[i]; }
            }

            X = _sx / _bx.Length;
            Y = _sy / _by.Length;
        }

        /// <summary>Échantillon le plus ancien de la fenêtre (celui qui sortira au prochain Push).</summary>
        public double OldestX => _bx[_head];
        public double OldestY => _by[_head];
    }

    /// <summary>
    /// Filtre anti-tremblement, alimenté à cadence fixe (<see cref="TickMs"/>) par la
    /// position voulue V (déplacements de la main × vitesse).
    ///
    ///  - Moyenne glissante DOUBLE (deux moyennes de durée W en cascade, soit une
    ///    pondération triangulaire sur 2W) : un tremblement de période W est annulé
    ///    exactement, ses harmoniques aussi, et les fréquences voisines sont
    ///    atténuées de 95 % ou plus. Le mouvement volontaire, lent comparé au
    ///    tremblement, passe intact avec un retard d'environ W.
    ///  - Réactivité : le déplacement NET sur la fenêtre (le tremblement, qui oscille
    ///    sur place, a un déplacement net quasi nul) mesure la vitesse volontaire.
    ///    Au-delà de 200 px/s, la sortie glisse progressivement vers une moyenne
    ///    4× plus courte : moins de retard dans les grands gestes, filtrage complet
    ///    pour le pointage fin.
    ///  - Quand la main s'arrête, les deux moyennes convergent exactement vers V :
    ///    le curseur arrive pile à l'endroit voulu, sans dérive ni perte de distance.
    /// </summary>
    internal sealed class TremorFilter
    {
        public const double TickMs = 4.0;           // cadence d'échantillonnage (250 Hz)
        public const double ReactiveFromPxS = 200;  // vitesse nette où la réactivité commence
        public const double ReactiveFullPxS = 1000; // vitesse nette où elle est complète
        private const double WeightTauMs = 40;      // lissage de la pondération réactive

        private readonly MovingAverage _heavy1 = new MovingAverage(), _heavy2 = new MovingAverage();
        private readonly MovingAverage _light1 = new MovingAverage(), _light2 = new MovingAverage();
        private readonly MovingAverage _history = new MovingAverage(); // V brut sur la fenêtre (vitesse nette)
        private int _windowTicks;
        private double _reactivity;
        private bool _bypass;

        /// <summary>Position filtrée (sortie).</summary>
        public double X { get; private set; }
        public double Y { get; private set; }

        /// <summary>Pondération réactive courante (0 = moyenne complète, 1 = moyenne courte).</summary>
        public double Weight { get; private set; }

        /// <summary>Vitesse volontaire estimée (px/s), sans le tremblement.</summary>
        public double NetSpeed { get; private set; }

        public int WindowTicks => _windowTicks;

        public TremorFilter()
        {
            _windowTicks = 1;
            Reset(0, 0);
        }

        /// <summary>
        /// Applique un réglage. Un changement de durée ne fait pas sauter le curseur :
        /// chaque étage repart de sa sortie courante.
        /// </summary>
        public void Configure(SmoothConfig cfg)
        {
            _reactivity = cfg.Reactivity;
            _bypass = cfg.SmoothingMs < TickMs;
            int n = Math.Max(1, (int)Math.Round(cfg.SmoothingMs / TickMs));
            if (n == _windowTicks) return;

            _windowTicks = n;
            int light = Math.Max(1, n / 4);
            _heavy1.Reset(n, _heavy1.X, _heavy1.Y);
            _heavy2.Reset(n, _heavy2.X, _heavy2.Y);
            _light1.Reset(light, _light1.X, _light1.Y);
            _light2.Reset(light, _light2.X, _light2.Y);
            _history.Reset(n, _history.OldestX, _history.OldestY);
        }

        /// <summary>Ré-ancre tout le filtre sur une position (sortie = position, aucun retard).</summary>
        public void Reset(double x, double y)
        {
            int light = Math.Max(1, _windowTicks / 4);
            _heavy1.Reset(_windowTicks, x, y);
            _heavy2.Reset(_windowTicks, x, y);
            _light1.Reset(light, x, y);
            _light2.Reset(light, x, y);
            _history.Reset(_windowTicks, x, y);
            X = x; Y = y;
            Weight = 0;
            NetSpeed = 0;
        }

        /// <summary>Un pas d'échantillonnage : V courant en entrée, sortie dans X/Y.</summary>
        public void Push(double vx, double vy)
        {
            // Vitesse nette : V maintenant contre V il y a W (le plus ancien de la fenêtre).
            double dx = vx - _history.OldestX, dy = vy - _history.OldestY;
            NetSpeed = Math.Sqrt(dx * dx + dy * dy) / (_windowTicks * TickMs / 1000.0);
            _history.Push(vx, vy);

            _heavy1.Push(vx, vy);
            _heavy2.Push(_heavy1.X, _heavy1.Y);
            _light1.Push(vx, vy);
            _light2.Push(_light1.X, _light1.Y);

            if (_bypass)
            {
                X = vx; Y = vy;
                Weight = 0;
                return;
            }

            double u = (NetSpeed - ReactiveFromPxS) / (ReactiveFullPxS - ReactiveFromPxS);
            u = Math.Min(1, Math.Max(0, u));
            double target = _reactivity * u * u * (3 - 2 * u); // smoothstep
            Weight += (target - Weight) * (TickMs / (TickMs + WeightTauMs));

            X = _heavy2.X + (_light2.X - _heavy2.X) * Weight;
            Y = _heavy2.Y + (_light2.Y - _heavy2.Y) * Weight;
        }

        /// <summary>Vrai quand la sortie a rejoint V (plus rien à animer : le moteur peut dormir).</summary>
        public bool IsSettled(double vx, double vy) =>
            Math.Abs(X - vx) < 0.01 && Math.Abs(Y - vy) < 0.01 &&
            Math.Abs(_heavy1.X - vx) < 0.01 && Math.Abs(_heavy1.Y - vy) < 0.01 &&
            Math.Abs(_history.OldestX - vx) < 0.01 && Math.Abs(_history.OldestY - vy) < 0.01;

        /// <summary>
        /// Arrondi au pixel avec hystérésis : le pixel affiché ne change que si la
        /// position filtrée s'en écarte de plus de 0,7 px. Évite le scintillement
        /// d'un pixel quand un résidu de tremblement oscille autour d'une frontière.
        /// </summary>
        public static int Quantize(int current, double value)
        {
            return Math.Abs(value - current) > 0.7 ? (int)Math.Round(value) : current;
        }
    }
}
