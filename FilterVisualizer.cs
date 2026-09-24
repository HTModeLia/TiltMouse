using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MouseSmooth
{
    /// <summary>
    /// Visualiseur temps réel du filtre, par axe :
    ///  - le cercle gris est la position ENGAGÉE C (ce que le filtre a validé) ;
    ///  - le triangle coloré est la position VIRTUELLE V (consigne matérielle) :
    ///    orange quand la porte est fermée (tremblement absorbé), vert quand elle
    ///    est ouverte (suivi natif) ;
    ///  - la ligne entre les deux est l'écart V−C qui commande l'ouverture ;
    ///  - la jauge à droite affiche l'ouverture de la porte (0 = gelé, 1 = natif).
    /// L'échelle s'adapte automatiquement à l'amplitude de l'écart.
    /// </summary>
    internal sealed class FilterVisualizer : Panel
    {
        private readonly Func<FilterState> _getState;
        private readonly Func<bool> _getActive;

        public FilterVisualizer(Func<FilterState> getState, Func<bool> getActive)
        {
            _getState = getState;
            _getActive = getActive;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = 118;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SystemColors.Control);

            if (!_getActive())
            {
                using var f = new Font("Segoe UI", 9f, FontStyle.Italic);
                using var b = new SolidBrush(Color.DimGray);
                var sz = g.MeasureString("Lissage inactif", f);
                g.DrawString("Lissage inactif", f, b, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
                return;
            }

            var st = _getState();
            int rowH = Height / 2;
            DrawAxis(g, new RectangleF(8, 4, Width - 16, rowH - 8), "X",
                     st.VX, st.CX, st.GateX);
            DrawAxis(g, new RectangleF(8, rowH + 4, Width - 16, rowH - 8), "Y",
                     st.VY, st.CY, st.GateY);
        }

        private void DrawAxis(Graphics g, RectangleF r, string axis, float v, float c, float gate)
        {
            float gap = v - c;

            // Échelle adaptative : au moins ±60 px, sinon 1,6× l'écart observé.
            float range = Math.Max(60f, Math.Abs(gap) * 1.6f);

            // La piste est centrée sur la position APPLIQUÉE au curseur (Out).
            float outPos = axis == "X" ? _getState().OutX : _getState().OutY;
            float Pix(float delta) => r.Left + r.Width / 2f + delta / range * (r.Width / 2f - 14f);
            float cy = r.Top + r.Height / 2f;

            // Piste.
            using (var pen = new Pen(Color.Gainsboro, 2f))
                g.DrawLine(pen, r.Left + 10, cy, r.Right - 10, cy);

            // Ligne d'écart C -> V.
            float xC = Pix(c - outPos), xV = Pix(v - outPos);
            using (var pen = new Pen(Color.SlateGray, 1.5f))
                g.DrawLine(pen, xC, cy, xV, cy);

            // Position engagée C : disque gris.
            using (var b = new SolidBrush(Color.Gray))
                g.FillEllipse(b, xC - 5, cy - 5, 10, 10);

            // Position virtuelle V : triangle orange (gelé) -> vert (natif).
            Color gateColor = Color.FromArgb(
                (int)(255 * (1 - gate) + 34 * gate),     // rouge -> vert
                (int)(140 * (1 - gate) + 160 * gate),
                (int)(40 * (1 - gate) + 34 * gate));
            var pts = new[]
            {
                new PointF(xV, cy - 7),
                new PointF(xV, cy + 7),
                new PointF(xV + 10, cy)
            };
            using (var b = new SolidBrush(gateColor))
                g.FillPolygon(b, pts);

            // Jauge d'ouverture de la porte.
            var gaugeRect = new RectangleF(r.Right - 64, cy - 6, 54, 12);
            g.DrawRectangle(Pens.Gray, gaugeRect.X, gaugeRect.Y, gaugeRect.Width, gaugeRect.Height);
            using (var b = new SolidBrush(gateColor))
                g.FillRectangle(b, gaugeRect.X, gaugeRect.Y, gaugeRect.Width * Clamp01(gate), gaugeRect.Height);

            // Textes : axe, écart, état.
            using (var f = new Font("Segoe UI", 8.25f))
            using (var b = new SolidBrush(SystemColors.ControlText))
            {
                g.DrawString(axis, f, b, r.Left, cy - 8);
                string state = gate < 0.05f ? "figé" : (gate > 0.95f ? "suivi natif" : "filtré");
                string txt = $"écart {gap,5:0.#} px   porte {gate * 100f,3:0}%  ({state})";
                var sz = g.MeasureString(txt, f);
                g.DrawString(txt, f, b, gaugeRect.Left - sz.Width - 8, cy - sz.Height / 2);
            }
        }

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    }
}
