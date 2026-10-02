using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MPU6050Monitor
{
    // Wykres dla żyroskopu jest rozdzielony od wykresu akcelerometru, bo używa innych jednostek,
    // innego domyślnego zakresu oraz innego opisu pustego stanu. Konstrukcja pozostaje jednak
    // spójna: trzy serie odpowiadają osiom X, Y i Z, a dane są rysowane z historią 3 minut.
    internal sealed class GyroscopePlot : Control
    {
        private static readonly Color GridColor = Color.FromArgb(226, 233, 231);
        private static readonly Color AxisXColor = Color.FromArgb(8, 126, 120);
        private static readonly Color AxisYColor = Color.FromArgb(207, 91, 79);
        private static readonly Color AxisZColor = Color.FromArgb(205, 142, 39);
        private readonly Queue<(DateTime Timestamp, double X, double Y, double Z)> _samples = new();
        private const int MaximumSamples = 360;
        private static readonly TimeSpan HistoryDuration = TimeSpan.FromMinutes(3);
        private double _rangeDegreesPerSecond = 250.0;

        public GyroscopePlot()
        {
            BackColor = Color.White;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void AddSample(DateTime timestamp, double x, double y, double z)
        {
            _samples.Enqueue((timestamp, x, y, z));
            var cutoff = timestamp - HistoryDuration;
            while (_samples.Count > 0 && _samples.Peek().Timestamp < cutoff)
            {
                _samples.Dequeue();
            }

            while (_samples.Count > MaximumSamples)
            {
                _samples.Dequeue();
            }

            Invalidate();
        }

        public void ClearSamples()
        {
            _samples.Clear();
            Invalidate();
        }

        public void SetRange(double fullScaleDegreesPerSecond)
        {
            if (fullScaleDegreesPerSecond <= 0.0)
            {
                return;
            }

            _rangeDegreesPerSecond = fullScaleDegreesPerSecond;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            var plot = new RectangleF(46, 30, ClientSize.Width - 58, ClientSize.Height - 62);
            if (plot.Width <= 20 || plot.Height <= 20)
            {
                return;
            }

            DrawLegend(graphics);
            DrawGrid(graphics, plot, _rangeDegreesPerSecond);

            if (_samples.Count == 0)
            {
                using var emptyFont = new Font("Segoe UI", 9);
                using var emptyBrush = new SolidBrush(Color.FromArgb(112, 129, 127));
                const string emptyText = "Oczekiwanie na próbki żyroskopu";
                var textSize = graphics.MeasureString(emptyText, emptyFont);
                graphics.DrawString(emptyText, emptyFont, emptyBrush,
                    plot.Left + (plot.Width - textSize.Width) / 2,
                    plot.Top + (plot.Height - textSize.Height) / 2);
                return;
            }

            var samples = _samples.ToArray();
            DrawSeries(graphics, plot, samples, sample => sample.X, AxisXColor, _rangeDegreesPerSecond);
            DrawSeries(graphics, plot, samples, sample => sample.Y, AxisYColor, _rangeDegreesPerSecond);
            DrawSeries(graphics, plot, samples, sample => sample.Z, AxisZColor, _rangeDegreesPerSecond);
            DrawTimeLabels(graphics, plot, samples);
        }

        private static void DrawLegend(Graphics graphics)
        {
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var xPen = new Pen(AxisXColor, 2.5f);
            using var yPen = new Pen(AxisYColor, 2.5f);
            using var zPen = new Pen(AxisZColor, 2.5f);
            using var brush = new SolidBrush(Color.FromArgb(29, 48, 50));
            DrawEntry("X", xPen, 46);
            DrawEntry("Y", yPen, 96);
            DrawEntry("Z", zPen, 146);

            void DrawEntry(string text, Pen pen, float left)
            {
                graphics.DrawLine(pen, left, 13, left + 15, 13);
                graphics.DrawString(text, font, brush, left + 20, 5);
            }
        }

        private static void DrawGrid(Graphics graphics, RectangleF plot, double rangeDegreesPerSecond)
        {
            using var gridPen = new Pen(GridColor, 1);
            using var zeroPen = new Pen(Color.FromArgb(153, 173, 169), 1)
            {
                DashStyle = DashStyle.Dash
            };
            using var labelFont = new Font("Segoe UI", 8);
            using var labelBrush = new SolidBrush(Color.FromArgb(112, 129, 127));

            var gridStep = rangeDegreesPerSecond / 4.0;
            for (var value = -rangeDegreesPerSecond; value <= rangeDegreesPerSecond + gridStep * 0.001; value += gridStep)
            {
                var y = ValueToY(value, plot, rangeDegreesPerSecond);
                var isZeroReference = Math.Abs(value) < 0.001;
                graphics.DrawLine(isZeroReference ? zeroPen : gridPen, plot.Left, y, plot.Right, y);
                graphics.DrawString(value.ToString("F0", CultureInfo.CurrentCulture),
                    labelFont, labelBrush, 2, y - 8);
            }

            graphics.DrawLine(gridPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
            graphics.DrawLine(gridPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        }

        private static void DrawSeries(
            Graphics graphics,
            RectangleF plot,
            (DateTime Timestamp, double X, double Y, double Z)[] samples,
            Func<(DateTime Timestamp, double X, double Y, double Z), double> selector,
            Color color,
            double rangeDegreesPerSecond)
        {
            var firstTimestamp = samples[0].Timestamp;
            var duration = (samples[^1].Timestamp - firstTimestamp).TotalSeconds;
            var points = new PointF[samples.Length];
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                var x = duration <= 0
                    ? plot.Right
                    : plot.Left + (float)((sample.Timestamp - firstTimestamp).TotalSeconds / duration) * plot.Width;
                points[index] = new PointF(x, ValueToY(selector(sample), plot, rangeDegreesPerSecond));
            }

            var graphicsState = graphics.Save();
            graphics.SetClip(plot);
            using var pen = new Pen(color, 2);
            using var brush = new SolidBrush(color);
            if (points.Length > 1)
            {
                graphics.DrawLines(pen, points);
            }
            else
            {
                graphics.FillEllipse(brush, points[0].X - 3, points[0].Y - 3, 6, 6);
            }

            graphics.Restore(graphicsState);
        }

        private static float ValueToY(double value, RectangleF plot, double rangeDegreesPerSecond)
        {
            var clampedValue = Math.Clamp(value, -rangeDegreesPerSecond, rangeDegreesPerSecond);
            return plot.Bottom - (float)((clampedValue + rangeDegreesPerSecond) / (2.0 * rangeDegreesPerSecond)) * plot.Height;
        }

        private static void DrawTimeLabels(
            Graphics graphics,
            RectangleF plot,
            (DateTime Timestamp, double X, double Y, double Z)[] samples)
        {
            using var font = new Font("Segoe UI", 8);
            using var brush = new SolidBrush(Color.FromArgb(112, 129, 127));
            graphics.DrawString(samples[0].Timestamp.ToString("HH:mm:ss"), font, brush,
                plot.Left, plot.Bottom + 5);
            var lastLabel = samples[^1].Timestamp.ToString("HH:mm:ss");
            var lastSize = graphics.MeasureString(lastLabel, font);
            graphics.DrawString(lastLabel, font, brush, plot.Right - lastSize.Width, plot.Bottom + 5);
        }
    }
}