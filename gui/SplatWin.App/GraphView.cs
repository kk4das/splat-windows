using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>
/// Draws one SPLAT! graph from its gnuplot data, the way gnuplot would lay it out: SPLAT!'s own
/// title, axis labels, ranges and line titles, with a second axis on the right when SPLAT! uses one.
/// Moving the mouse over it shows each line's value at that distance.
/// </summary>
public sealed class GraphView : FrameworkElement
{
    // gnuplot 6's default line colours, in order.
    static readonly Color[] Palette =
    {
        Color.FromRgb(0x94, 0x00, 0xD3), Color.FromRgb(0x00, 0x9E, 0x73), Color.FromRgb(0x56, 0xB4, 0xE9),
        Color.FromRgb(0xE6, 0x9F, 0x00), Color.FromRgb(0xD5, 0x5E, 0x00), Color.FromRgb(0x00, 0x72, 0xB2), Color.FromRgb(0xE5, 0x1E, 0x10),
    };

    GraphData? _data;
    Point? _mouse;

    public GraphData? Data
    {
        get => _data;
        set
        {
            _data = value;
            AutomationProperties.SetName(this, value == null ? "No graph" :
                $"{value.Title.Replace('\n', ' ')}: {value.Series.Count} line(s), {value.Series.Sum(s => s.Points.Count)} points");
            InvalidateVisual();
        }
    }

    public static readonly DependencyProperty ForegroundProperty =
        System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(GraphView),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public GraphView()
    {
        ClipToBounds = true;
        MouseMove += (_, e) => { _mouse = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (_, _) => { _mouse = null; InvalidateVisual(); };
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new GraphViewPeer(this);

    sealed class GraphViewPeer(GraphView owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(GraphView);
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Image;
    }

    protected override Size MeasureOverride(Size available) => new(
        double.IsInfinity(available.Width) ? 640 : available.Width,
        double.IsInfinity(available.Height) ? 400 : available.Height);

    FormattedText Text(string s, double size, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Round tick steps (1, 2, 5 × 10^n) giving about <paramref name="want"/> intervals.</summary>
    static double Step(double span, int want)
    {
        if (!(span > 0)) return 1;
        double raw = span / want, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / mag;
        return (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10) * mag;
    }

    static (double, double) Auto(IEnumerable<double> values)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var v in values) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        if (lo > hi) return (0, 1);
        if (lo == hi) { lo -= 1; hi += 1; }
        double s = Step(hi - lo, 6);   // gnuplot autoscale extends to the next tick
        return (Math.Floor(lo / s) * s, Math.Ceiling(hi / s) * s);
    }

    static string Num(double v, double step)
    {
        if (Math.Abs(v) < step * 1e-6) v = 0;   // no "-0.0" from rounding
        int dec = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step) + 1e-9));
        return v.ToString("F" + Math.Min(dec, 6), CultureInfo.CurrentCulture);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));   // so the mouse is seen everywhere
        var d = _data;
        if (d == null || W < 120 || H < 120) return;

        bool y2 = d.Series.Any(s => s.RightAxis);
        var title = Text(d.Title, 13, true);
        title.TextAlignment = TextAlignment.Center;
        var legend = d.Series.Select((s, i) => (s, i)).Where(t => t.s.Title.Length > 0).ToList();
        double legendRows = Math.Ceiling(legend.Count / 2.0);
        double left = 74, right = y2 ? 74 : 24, top = title.Height + 14, bottom = 44 + legendRows * 18;
        var plot = new Rect(left, top, Math.Max(10, W - left - right), Math.Max(10, H - top - bottom));

        var (x0, x1) = d.XRange ?? Auto(d.Series.SelectMany(s => s.Points.Select(p => p.X)));
        var (ya, yb) = d.YRange ?? Auto(d.Series.Where(s => !s.RightAxis).SelectMany(s => s.Points.Select(p => p.Y)));
        var (za, zb) = d.Y2Range ?? Auto(d.Series.Where(s => s.RightAxis).SelectMany(s => s.Points.Select(p => p.Y)));
        if (x1 <= x0) x1 = x0 + 1; if (yb <= ya) yb = ya + 1; if (zb <= za) zb = za + 1;
        double X(double v) => plot.Left + (v - x0) / (x1 - x0) * plot.Width;
        double Y(double v, bool r) => r ? plot.Bottom - (v - za) / (zb - za) * plot.Height : plot.Bottom - (v - ya) / (yb - ya) * plot.Height;

        var fg = Foreground;
        var axis = new Pen(fg, 1);
        var grid = new Pen(fg, 0.5) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };
        grid.Brush = fg.Clone(); grid.Brush.Opacity = 0.45;

        dc.DrawText(title, new Point(W / 2, 6));

        // Ticks and grid.
        double sx = Step(x1 - x0, Math.Max(2, (int)(plot.Width / 90)));
        for (double v = Math.Ceiling(x0 / sx) * sx; v <= x1 + sx * 1e-9; v += sx)
        {
            double px = X(v);
            dc.DrawLine(grid, new Point(px, plot.Top), new Point(px, plot.Bottom));
            var t = Text(Num(v, sx), 11);
            dc.DrawText(t, new Point(px - t.Width / 2, plot.Bottom + 4));
        }
        double sy = Step(yb - ya, Math.Max(2, (int)(plot.Height / 45)));
        for (double v = Math.Ceiling(ya / sy) * sy; v <= yb + sy * 1e-9; v += sy)
        {
            double py = Y(v, false);
            dc.DrawLine(grid, new Point(plot.Left, py), new Point(plot.Right, py));
            var t = Text(Num(v, sy), 11);
            dc.DrawText(t, new Point(plot.Left - t.Width - 5, py - t.Height / 2));
        }
        if (y2)
        {
            double sz = Step(zb - za, Math.Max(2, (int)(plot.Height / 45)));
            for (double v = Math.Ceiling(za / sz) * sz; v <= zb + sz * 1e-9; v += sz)
            {
                double py = Y(v, true);
                dc.DrawLine(axis, new Point(plot.Right, py), new Point(plot.Right - 5, py));
                dc.DrawText(Text(Num(v, sz), 11), new Point(plot.Right + 5, py - 8));
            }
        }
        dc.DrawRectangle(null, axis, plot);

        // Axis labels.
        var xl = Text(d.XLabel, 12);
        dc.DrawText(xl, new Point(plot.Left + (plot.Width - xl.Width) / 2, plot.Bottom + 20));
        var yl = Text(d.YLabel, 12);
        yl.TextAlignment = TextAlignment.Center;
        dc.PushTransform(new RotateTransform(-90, 14, plot.Top + plot.Height / 2));
        dc.DrawText(yl, new Point(14, plot.Top + plot.Height / 2 - 4));
        dc.Pop();

        // The lines, clipped to the plot.
        dc.PushClip(new RectangleGeometry(plot));
        for (int i = 0; i < d.Series.Count; i++)
        {
            var s = d.Series[i];
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(s.Points[0].X), Y(s.Points[0].Y, s.RightAxis)), false, false);
                g.PolyLineTo(s.Points.Skip(1).Select(p => new Point(X(p.X), Y(p.Y, s.RightAxis))).ToList(), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Palette[i % Palette.Length]), 1.6), geo);
        }
        dc.Pop();

        // Legend under the x label, two per row.
        for (int k = 0; k < legend.Count; k++)
        {
            var (s, i) = legend[k];
            double lx = plot.Left + (k % 2) * plot.Width / 2, ly = plot.Bottom + 40 + (k / 2) * 18;
            dc.DrawLine(new Pen(new SolidColorBrush(Palette[i % Palette.Length]), 2.5), new Point(lx, ly + 8), new Point(lx + 26, ly + 8));
            dc.DrawText(Text(s.Title + (s.RightAxis ? " (right axis)" : ""), 11), new Point(lx + 32, ly));
        }

        // Readout at the mouse.
        if (_mouse is { } m && plot.Contains(m))
        {
            double xv = x0 + (m.X - plot.Left) / plot.Width * (x1 - x0);
            dc.DrawLine(axis, new Point(m.X, plot.Top), new Point(m.X, plot.Bottom));
            var lines = new List<string> { $"x = {xv.ToString("0.###", CultureInfo.CurrentCulture)}" };
            foreach (var s in d.Series)
                if (Interpolate(s.Points, xv) is double yv)
                    lines.Add($"{(s.Title.Length > 0 ? s.Title : "value")}: {yv.ToString("0.###", CultureInfo.CurrentCulture)}");
            var t = Text(string.Join("\n", lines), 11);
            double tx = m.X + 10 + t.Width > plot.Right ? m.X - 10 - t.Width : m.X + 10;
            var box = new Rect(tx - 4, plot.Top + 6, t.Width + 8, t.Height + 6);
            var bg = (SystemParameters.HighContrast ? SystemColors.WindowBrush : (Brush)new SolidColorBrush(Color.FromArgb(0xE8, 0x80, 0x80, 0x80)));
            dc.DrawRoundedRectangle(bg, null, box, 3, 3);
            dc.DrawText(t, new Point(tx, plot.Top + 9));
        }
    }

    static double? Interpolate(IReadOnlyList<(double X, double Y)> pts, double x)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            var (ax, ay) = pts[i - 1]; var (bx, by) = pts[i];
            if ((x >= ax && x <= bx) || (x >= bx && x <= ax))
                return bx == ax ? ay : ay + (by - ay) * (x - ax) / (bx - ax);
        }
        return null;
    }
}
