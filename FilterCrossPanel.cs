using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Visualiseur en croix du filtre (axe X horizontal, axe Y vertical) :
    ///  - disque gris : position ENGAGÉE C (ce que le filtre a validé) ;
    ///  - triangle coloré : position VIRTUELLE V (consigne matérielle) — orange
    ///    quand la porte est fermée (tremblement absorbé), vert en suivi natif ;
    ///  - cercle bleu pointillé : seuil de tremblement autour de C (V dedans =>
    ///    porte fermée, curseur gelé ; V dehors => le suivi reprend) ;
    ///  - colonne de droite : valeurs numériques par axe (positions, écart,
    ///    ouverture de porte) et rappel seuil/soupape.
    /// Échelle automatique : au moins ±60 px, sinon 1,6× l'amplitude observée.
    /// </summary>
    internal sealed class FilterCrossPanel : Panel
    {
        private readonly Func<FilterState> _getState;
        private readonly Func<SmoothConfig> _getConfig;
        private readonly Func<bool> _getActive;
        private readonly Func<bool> _getPassThrough;

        public FilterCrossPanel(Func<FilterState> getState, Func<SmoothConfig> getConfig,
                                Func<bool> getActive, Func<bool> getPassThrough)
        {
            _getState = getState;
            _getConfig = getConfig;
            _getActive = getActive;
            _getPassThrough = getPassThrough;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = 178;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SystemColors.Control);

            var cfg = _getConfig();
            bool observing = _getPassThrough() && cfg.ValidMode;

            if (observing)
            {
                DrawHeader(g, "Observation (utilisateur valide) : souris native, filtre simulé");
            }
            else if (_getPassThrough())
            {
                DrawCentered(g, "Mode natif : réglages neutres, rien à visualiser");
                return;
            }
            else if (!_getActive())
            {
                DrawCentered(g, "Lissage inactif");
                return;
            }

            var st = _getState();
            int square = Math.Min(Height - 10, 168);
            var plot = new RectangleF(8, (Height - square) / 2f + 4, square, square);
            DrawCross(g, plot, st, cfg);
            DrawValues(g, new RectangleF(plot.Right + 10, 6, Width - plot.Right - 18, Height - 12),
                       st, cfg, observing);
        }

        /// <summary>Croix centrale : axes, zone de gel (seuil), C, V et liaison C→V.</summary>
        private void DrawCross(Graphics g, RectangleF r, FilterState st, SmoothConfig cfg)
        {
            float ox = st.OutX, oy = st.OutY;
            float amp = Math.Max(60f, 1.6f * Math.Max(
                Math.Max(Math.Abs(st.VX - ox), Math.Abs(st.VY - oy)),
                Math.Max(Math.Abs(st.CX - ox), Math.Abs(st.CY - oy))));
            float half = r.Width / 2f - 14f;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float Px(float dx) => cx + dx / amp * half;
            float Py(float dy) => cy - dy / amp * half; // axe Y écran inversé

            // Axes de la croix.
            using (var pen = new Pen(Color.Silver, 1f))
            {
                g.DrawLine(pen, r.Left + 4, cy, r.Right - 4, cy);
                g.DrawLine(pen, cx, r.Top + 4, cx, r.Bottom - 4);
            }
            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(Color.Gray))
            {
                g.DrawString("X", f, b, r.Right - 13, cy - 15);
                g.DrawString("Y", f, b, cx + 4, r.Top + 2);
            }

            // Cercle du seuil autour de C : V à l'intérieur => tremblement absorbé.
            float xc = Px(st.CX - ox), yc = Py(st.CY - oy);
            float thr = (float)Math.Max(0.0, cfg.TremorThreshold) / amp * half;
            using (var pen = new Pen(Color.FromArgb(130, 31, 111, 235), 1f) { DashStyle = DashStyle.Dash })
                g.DrawEllipse(pen, xc - thr, yc - thr, thr * 2f, thr * 2f);

            // Liaison C -> V (l'écart qui commande la porte).
            float xv = Px(st.VX - ox), yv = Py(st.VY - oy);
            using (var pen = new Pen(Color.SlateGray, 1.5f))
                g.DrawLine(pen, xc, yc, xv, yv);

            // Position engagée C : disque gris.
            using (var b = new SolidBrush(Color.Gray))
                g.FillEllipse(b, xc - 5, yc - 5, 10, 10);

            // Position virtuelle V : triangle orange (gelé) -> vert (natif).
            using (var b = new SolidBrush(GateColor((st.GateX + st.GateY) / 2f)))
            {
                var pts = new[] { new PointF(xv, yv - 7), new PointF(xv, yv + 7), new PointF(xv + 10, yv) };
                g.FillPolygon(b, pts);
            }
        }

        /// <summary>Colonne de valeurs numériques, par axe.</summary>
        private void DrawValues(Graphics g, RectangleF r, FilterState st, SmoothConfig cfg, bool observing)
        {
            using (var f = new Font("Consolas", 8.25f))
            using (var b = new SolidBrush(SystemColors.ControlText))
            {
                float y = r.Top;
                y = DrawAxisInfo(g, f, b, r, y, "X", st.VX, st.CX, st.GateX);
                y = DrawAxisInfo(g, f, b, r, y + 6, "Y", st.VY, st.CY, st.GateY);
                float thr = (float)cfg.TremorThreshold;
                g.DrawString($"Seuil {thr:0} px  soupape {thr * 2f:0} px", f, b, r.Left, y + 6);
                if (observing)
                {
                    using var b2 = new SolidBrush(Color.FromArgb(31, 111, 235));
                    g.DrawString("(valeurs simulées : curseur natif)", f, b2, r.Left, y + 22);
                }
            }
        }

        private float DrawAxisInfo(Graphics g, Font f, Brush b, RectangleF r, float y,
            string axis, float v, float c, float gate)
        {
            g.DrawString($"Axe {axis}", f, b, r.Left, y);
            g.DrawString($"C {c,7:0.#}  V {v,7:0.#}", f, b, r.Left, y + 14);
            string state = gate < 0.05f ? "figé" : (gate > 0.95f ? "natif" : "filtré");
            g.DrawString($"écart {Math.Abs(v - c),5:0.#}  porte {gate * 100f,3:0}% {state}",
                         f, b, r.Left, y + 28);
            return y + 42;
        }

        private static Color GateColor(float gate) => Color.FromArgb(
            (int)(255 * (1 - gate) + 34 * gate),
            (int)(140 * (1 - gate) + 160 * gate),
            (int)(40 * (1 - gate) + 34 * gate));

        private void DrawHeader(Graphics g, string text)
        {
            using var f = new Font("Segoe UI", 8.25f, FontStyle.Italic);
            using var b = new SolidBrush(Color.FromArgb(31, 111, 235));
            g.DrawString(text, f, b, 8, 2);
        }

        private void DrawCentered(Graphics g, string text)
        {
            using var f = new Font("Segoe UI", 9f, FontStyle.Italic);
            using var b = new SolidBrush(Color.DimGray);
            var sz = g.MeasureString(text, f);
            g.DrawString(text, f, b, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
        }
    }
}
