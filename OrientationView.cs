using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MPU6050Monitor
{
    // Widok 3D orientacji czujnika. Orientacja jest liczona filtrem komplementarnym na kwaternionie
    // (Mahony bez członu całkującego): żyroskop daje szybkie zmiany, a kierunek grawitacji z akcelerometru
    // powoli koryguje dryf roll/pitch. Yaw pochodzi wyłącznie z żyroskopu, więc dryfuje.
    internal sealed class OrientationView : Control
    {
        private const double CorrectionGain = 2.0;
        private const double MaximumStepSeconds = 0.5;
        private const double GravityTolerance = 0.25;
        private const double ViewAzimuth = -0.55;
        private const double ViewElevation = 0.5;
        private const double CameraDistance = 7.0;
        private const double RadiansToDegrees = 180.0 / Math.PI;

        private static readonly Color AxisXColor = Color.FromArgb(8, 126, 120);
        private static readonly Color AxisYColor = Color.FromArgb(207, 91, 79);
        private static readonly Color AxisZColor = Color.FromArgb(205, 142, 39);
        private static readonly Color GridColor = Color.FromArgb(226, 233, 231);
        private static readonly Color InkColor = Color.FromArgb(29, 48, 50);
        private static readonly Color MutedColor = Color.FromArgb(112, 129, 127);

        private static readonly Vec3[] BoxVertices = BuildBoxVertices(1.0, 0.65, 0.1);

        // Wierzchołki: bit 0 = +X, bit 1 = +Y, bit 2 = +Z.
        private static readonly (int[] Indices, Color Fill)[] BoxFaces =
        {
            (new[] { 4, 5, 7, 6 }, Color.FromArgb(8, 126, 120)),
            (new[] { 0, 2, 3, 1 }, Color.FromArgb(150, 164, 162)),
            (new[] { 1, 3, 7, 5 }, Color.FromArgb(190, 205, 202)),
            (new[] { 0, 4, 6, 2 }, Color.FromArgb(190, 205, 202)),
            (new[] { 2, 6, 7, 3 }, Color.FromArgb(172, 188, 185)),
            (new[] { 0, 1, 5, 4 }, Color.FromArgb(172, 188, 185))
        };

        private double _w = 1.0;
        private double _x;
        private double _y;
        private double _z;
        private DateTime? _lastTimestamp;
        private bool _hasData;

        public OrientationView()
        {
            BackColor = Color.White;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void AddSample(
            DateTime timestamp,
            double accelXG, double accelYG, double accelZG,
            double gyroXDps, double gyroYDps, double gyroZDps)
        {
            var magnitude = Math.Sqrt(accelXG * accelXG + accelYG * accelYG + accelZG * accelZG);
            var step = _lastTimestamp is { } last ? (timestamp - last).TotalSeconds : double.NaN;
            _lastTimestamp = timestamp;

            if (!(step <= MaximumStepSeconds))
            {
                if (magnitude > 0.1)
                {
                    InitializeFromAccelerometer(accelXG, accelYG, accelZG);
                    _hasData = true;
                    Invalidate();
                }

                return;
            }

            if (step <= 0.0)
            {
                return;
            }

            var gx = gyroXDps / RadiansToDegrees;
            var gy = gyroYDps / RadiansToDegrees;
            var gz = gyroZDps / RadiansToDegrees;

            // Korekta tylko gdy |a| ≈ 1 g, czyli czujnik nie jest gwałtownie przyspieszany.
            if (Math.Abs(magnitude - 1.0) <= GravityTolerance)
            {
                var ax = accelXG / magnitude;
                var ay = accelYG / magnitude;
                var az = accelZG / magnitude;
                var vx = 2.0 * (_x * _z - _w * _y);
                var vy = 2.0 * (_w * _x + _y * _z);
                var vz = 1.0 - 2.0 * (_x * _x + _y * _y);
                gx += CorrectionGain * (ay * vz - az * vy);
                gy += CorrectionGain * (az * vx - ax * vz);
                gz += CorrectionGain * (ax * vy - ay * vx);
            }

            var dw = -(_x * gx + _y * gy + _z * gz);
            var dx = _w * gx + _y * gz - _z * gy;
            var dy = _w * gy + _z * gx - _x * gz;
            var dz = _w * gz + _x * gy - _y * gx;
            _w += 0.5 * step * dw;
            _x += 0.5 * step * dx;
            _y += 0.5 * step * dy;
            _z += 0.5 * step * dz;

            var norm = Math.Sqrt(_w * _w + _x * _x + _y * _y + _z * _z);
            _w /= norm;
            _x /= norm;
            _y /= norm;
            _z /= norm;

            _hasData = true;
            Invalidate();
        }

        public void Reset()
        {
            _w = 1.0;
            _x = 0.0;
            _y = 0.0;
            _z = 0.0;
            _lastTimestamp = null;
            _hasData = false;
            Invalidate();
        }

        private void InitializeFromAccelerometer(double ax, double ay, double az)
        {
            var roll = Math.Atan2(ay, az);
            var pitch = Math.Atan2(-ax, Math.Sqrt(ay * ay + az * az));
            var cr = Math.Cos(roll / 2.0);
            var sr = Math.Sin(roll / 2.0);
            var cp = Math.Cos(pitch / 2.0);
            var sp = Math.Sin(pitch / 2.0);
            _w = cr * cp;
            _x = sr * cp;
            _y = cr * sp;
            _z = -sr * sp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            if (ClientSize.Width < 60 || ClientSize.Height < 60)
            {
                return;
            }

            var scale = (float)(Math.Min(ClientSize.Width, ClientSize.Height) / 4.2);
            var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f + 10f);

            DrawGround(graphics, center, scale);
            DrawBox(graphics, center, scale);
            DrawBodyAxes(graphics, center, scale);
            DrawReadout(graphics);
        }

        private void DrawGround(Graphics graphics, PointF center, float scale)
        {
            using var pen = new Pen(GridColor, 1f);
            const double Extent = 2.0;
            const double Level = -1.3;
            for (var i = -2; i <= 2; i++)
            {
                graphics.DrawLine(pen,
                    Project(new Vec3(i, -Extent, Level), center, scale, out _),
                    Project(new Vec3(i, Extent, Level), center, scale, out _));
                graphics.DrawLine(pen,
                    Project(new Vec3(-Extent, i, Level), center, scale, out _),
                    Project(new Vec3(Extent, i, Level), center, scale, out _));
            }
        }

        private void DrawBox(Graphics graphics, PointF center, float scale)
        {
            var points = new PointF[BoxVertices.Length];
            var depths = new double[BoxVertices.Length];
            for (var i = 0; i < BoxVertices.Length; i++)
            {
                points[i] = Project(Rotate(BoxVertices[i]), center, scale, out depths[i]);
            }

            var order = new (int Face, double Depth)[BoxFaces.Length];
            for (var f = 0; f < BoxFaces.Length; f++)
            {
                var sum = 0.0;
                foreach (var index in BoxFaces[f].Indices)
                {
                    sum += depths[index];
                }

                order[f] = (f, sum / 4.0);
            }

            Array.Sort(order, (a, b) => b.Depth.CompareTo(a.Depth));

            using var outline = new Pen(InkColor, 1.2f) { LineJoin = LineJoin.Round };
            foreach (var (face, _) in order)
            {
                var polygon = Array.ConvertAll(BoxFaces[face].Indices, index => points[index]);
                using var brush = new SolidBrush(BoxFaces[face].Fill);
                graphics.FillPolygon(brush, polygon);
                graphics.DrawPolygon(outline, polygon);
            }
        }

        private void DrawBodyAxes(Graphics graphics, PointF center, float scale)
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            var origin = Project(new Vec3(0, 0, 0), center, scale, out _);
            DrawAxis(graphics, font, origin, new Vec3(1.7, 0, 0), "X", AxisXColor, center, scale);
            DrawAxis(graphics, font, origin, new Vec3(0, 1.4, 0), "Y", AxisYColor, center, scale);
            DrawAxis(graphics, font, origin, new Vec3(0, 0, 1.2), "Z", AxisZColor, center, scale);
        }

        private void DrawAxis(
            Graphics graphics, Font font, PointF origin, Vec3 bodyEnd, string label, Color color,
            PointF center, float scale)
        {
            var end = Project(Rotate(bodyEnd), center, scale, out _);
            using var pen = new Pen(color, 2.5f) { EndCap = LineCap.ArrowAnchor };
            using var brush = new SolidBrush(color);
            graphics.DrawLine(pen, origin, end);
            graphics.DrawString(label, font, brush, end.X + 3f, end.Y - 16f);
        }

        private void DrawReadout(Graphics graphics)
        {
            var sinPitch = Math.Clamp(2.0 * (_w * _y - _z * _x), -1.0, 1.0);
            var roll = Math.Atan2(2.0 * (_w * _x + _y * _z), 1.0 - 2.0 * (_x * _x + _y * _y)) * RadiansToDegrees;
            var pitch = Math.Asin(sinPitch) * RadiansToDegrees;
            var yaw = Math.Atan2(2.0 * (_w * _z + _x * _y), 1.0 - 2.0 * (_y * _y + _z * _z)) * RadiansToDegrees;

            using var valueFont = new Font("Consolas", 11f);
            using var noteFont = new Font("Segoe UI", 8.5f);
            using var inkBrush = new SolidBrush(InkColor);
            using var mutedBrush = new SolidBrush(MutedColor);

            var text = string.Create(CultureInfo.CurrentCulture,
                $"Roll   {roll,7:+0.0;-0.0;0.0}°\nPitch  {pitch,7:+0.0;-0.0;0.0}°\nYaw    {yaw,7:+0.0;-0.0;0.0}°");
            graphics.DrawString(text, valueFont, inkBrush, 10f, 8f);

            var note = _hasData
                ? "Yaw z samego żyroskopu — dryfuje (reset: Wyczyść)"
                : "Oczekiwanie na dane z czujnika";
            graphics.DrawString(note, noteFont, mutedBrush, 10f, ClientSize.Height - 24f);
        }

        // Obrót z układu czujnika do układu świata (Z do góry).
        private Vec3 Rotate(Vec3 v)
        {
            var xx = _x * _x;
            var yy = _y * _y;
            var zz = _z * _z;
            return new Vec3(
                (1.0 - 2.0 * (yy + zz)) * v.X + 2.0 * (_x * _y - _w * _z) * v.Y + 2.0 * (_x * _z + _w * _y) * v.Z,
                2.0 * (_x * _y + _w * _z) * v.X + (1.0 - 2.0 * (xx + zz)) * v.Y + 2.0 * (_y * _z - _w * _x) * v.Z,
                2.0 * (_x * _z - _w * _y) * v.X + 2.0 * (_y * _z + _w * _x) * v.Y + (1.0 - 2.0 * (xx + yy)) * v.Z);
        }

        // Stała kamera: obrót wokół Z, pochylenie w dół i rzut perspektywiczny.
        private static PointF Project(Vec3 p, PointF center, float scale, out double depth)
        {
            var ca = Math.Cos(ViewAzimuth);
            var sa = Math.Sin(ViewAzimuth);
            var x = p.X * ca - p.Y * sa;
            var y = p.X * sa + p.Y * ca;
            var up = y * Math.Sin(ViewElevation) + p.Z * Math.Cos(ViewElevation);
            depth = y * Math.Cos(ViewElevation) - p.Z * Math.Sin(ViewElevation);
            var perspective = CameraDistance / (CameraDistance + depth);
            return new PointF(
                center.X + (float)(x * perspective) * scale,
                center.Y - (float)(up * perspective) * scale);
        }

        private static Vec3[] BuildBoxVertices(double hx, double hy, double hz)
        {
            var vertices = new Vec3[8];
            for (var i = 0; i < 8; i++)
            {
                vertices[i] = new Vec3(
                    (i & 1) != 0 ? hx : -hx,
                    (i & 2) != 0 ? hy : -hy,
                    (i & 4) != 0 ? hz : -hz);
            }

            return vertices;
        }

        private readonly record struct Vec3(double X, double Y, double Z);
    }
}
