using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Visualiseur : les 2 dernières secondes du mouvement de la main (orange, avec
    /// son tremblement) et du curseur lissé (vert), centrées sur le curseur, avec
    /// zoom automatique. On voit directement ce que la moyenne retire.
    /// </summary>
    internal sealed class TrajectoryView : Control
    {
        private static readonly Color Background = Color.FromArgb(250, 250, 250);
        private static readonly Color GridColor = Color.FromArgb(228, 228, 228);
        private static readonly Color HandColor = Color.FromArgb(230, 120, 30);
        private static readonly Color CursorColor = Color.FromArgb(40, 150, 60);

        private readonly MotionEngine _engine;
        private readonly Func<SmoothConfig> _config;
        private readonly List<TracePoint> _points = new List<TracePoint>(512);
        private readonly Timer _timer = new Timer { Interval = 33 };

        public TrajectoryView(MotionEngine engine, Func<SmoothConfig> config)
        {
            _engine = engine;
            _config = config;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _timer.Tick += (s, e) => Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _timer.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _timer.Stop();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            _engine.CopyTrace(_points);
            float cx = Width / 2f, cy = Height / 2f;

            // Zoom : la plus grande excursion récente occupe ~80 % de la demi-hauteur.
            float ox = 0, oy = 0, extent = 15f;
            if (_points.Count > 0)
            {
                var last = _points[_points.Count - 1];
                ox = last.CursorX; oy = last.CursorY;
                foreach (var p in _points)
                {
                    extent = Math.Max(extent, Math.Abs(p.HandX - ox));
                    extent = Math.Max(extent, Math.Abs(p.HandY - oy));
                    extent = Math.Max(extent, Math.Abs(p.CursorX - ox));
                    extent = Math.Max(extent, Math.Abs(p.CursorY - oy));
                }
            }
            float scale = Math.Min(6f, 0.8f * Math.Min(cx, cy) / extent);

            // Grille de 10 px écran (ou 100 px si fortement dézoomé).
            float step = 10 * scale >= 8 ? 10 : 100;
            using (var grid = new Pen(GridColor))
            {
                float sp = step * scale;
                float gx = cx - ((ox % step) * scale);
                for (float x = gx % sp; x < Width; x += sp) g.DrawLine(grid, x, 0, x, Height);
                float gy = cy - ((oy % step) * scale);
                for (float y = gy % sp; y < Height; y += sp) g.DrawLine(grid, 0, y, Width, y);
            }

            if (_points.Count > 1)
            {
                var hand = new PointF[_points.Count];
                var cursor = new PointF[_points.Count];
                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    hand[i] = new PointF(cx + (p.HandX - ox) * scale, cy + (p.HandY - oy) * scale);
                    cursor[i] = new PointF(cx + (p.CursorX - ox) * scale, cy + (p.CursorY - oy) * scale);
                }
                using (var handPen = new Pen(HandColor, 1.2f))
                    g.DrawLines(handPen, hand);
                using (var cursorPen = new Pen(CursorColor, 2.5f) { LineJoin = LineJoin.Round })
                    g.DrawLines(cursorPen, cursor);

                var h = hand[hand.Length - 1];
                using (var b = new SolidBrush(HandColor)) g.FillEllipse(b, h.X - 3, h.Y - 3, 6, 6);
                using (var b = new SolidBrush(CursorColor)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            }

            // Légende et état.
            var cfg = _config();
            using (var font = new Font("Segoe UI", 8.5f))
            using (var text = new SolidBrush(Color.FromArgb(70, 70, 70)))
            using (var handBrush = new SolidBrush(HandColor))
            using (var cursorBrush = new SolidBrush(CursorColor))
            {
                g.DrawString("— main (avec tremblement)", font, handBrush, 6, 4);
                g.DrawString("— curseur lissé", font, cursorBrush, 6, 20);
                string state = _engine.Enabled ? "lissage actif" : "souris native (simulation)";
                string info = $"{state}   ·   moyenne {cfg.SmoothingMs:0} ms   ·   " +
                              $"geste {_engine.NetSpeed:0} px/s   ·   réactivité {_engine.ReactiveWeight * 100:0} %   ·   " +
                              $"grille {step:0} px";
                g.DrawString(info, font, text, 6, Height - 20);
            }
        }
    }
}
