// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Fipinator.GaugeMaker;

/// <summary>
/// The opening screen (owner, 2026-11-14): a DC-6 (white top, red cheatline, bare-metal belly: our own drawing and
/// our own paint, not an airline's livery) crosses a dusk sky leaving cartoon smoke puffs that build a cloud.
/// "FIPinator 3000" appears in the cloud, and the three zeros are instruments that come alive: an airspeed
/// indicator, an altimeter and an attitude indicator. Then "A FREE gauge editor..." fades in. About 5 seconds;
/// a click or any key skips it.
/// </summary>
internal sealed class SplashWindow : Window
{
    private const double W = 760, H = 360;
    private const double FlyFrom = -260, FlyTo = 800, FlySeconds = 2.4, EndSeconds = FlySeconds + 2.6;
    private const double Speed = (FlyTo - FlyFrom) / FlySeconds;
    private const double NameSize = 72, Gap = 6, Baseline = 216;

    /// <summary>Dial radius inside the bezel: set so a whole dial is as tall as the digits (owner: the "3" matches the gauges).</summary>
    private double R = 22;

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x2B, 0x2F, 0x33));
    private static readonly Brush Smoke = Frozen(Color.FromRgb(0xF4, 0xF1, 0xE8));
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xF2, 0xA0, 0x3D));
    private static readonly Brush Cream = Frozen(Color.FromRgb(0xE8, 0xE5, 0xDC));

    private readonly Canvas _sky = new() { Width = W, Height = H, ClipToBounds = true };
    private readonly Canvas _plane;
    private readonly List<(Ellipse Outline, Ellipse Fill, double X, double Y, double R)> _puffs = [];
    private readonly Canvas _name = new() { Opacity = 0 };
    private readonly TextBlock _tagline;
    private readonly RotateTransform _asi = new(), _altLong = new(), _altShort = new();
    private readonly TransformGroup _horizon = new();
    private readonly RotateTransform _roll = new();
    private readonly TranslateTransform _pitch = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _closing;

    public SplashWindow()
    {
        Width = W;
        Height = H;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FIPinator 3000";
        _sky.Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x22, 0x33), Color.FromRgb(0x5A, 0x4A, 0x46), 90);

        MakePuffs();
        MakeName();
        _tagline = new TextBlock
        {
            Text = "A FREE gauge editor for Logitech FIPs  ·  open source  ·  fork it", FontSize = 17,
            Foreground = Amber, Width = W, TextAlignment = TextAlignment.Center, Opacity = 0,
        };
        Canvas.SetTop(_tagline, 292);
        _sky.Children.Add(_tagline);
        _plane = Dc6();
        Canvas.SetTop(_plane, 70);
        Canvas.SetLeft(_plane, FlyFrom);
        _sky.Children.Add(_plane);
        Content = _sky;

        CompositionTarget.Rendering += Frame;
        Closed += (_, _) => CompositionTarget.Rendering -= Frame;
        MouseDown += (_, _) => FadeAndClose();
        KeyDown += (_, _) => FadeAndClose();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------- the cloud ----------

    private void MakePuffs()
    {
        var seed = 7;
        double Rnd() { seed = (seed * 9301 + 49297) % 233280; return seed / 233280.0; }
        var outlines = new List<Ellipse>();
        var fills = new List<Ellipse>();
        for (var x = 46; x <= 714; x += 24)
        {
            var n = x < 106 || x > 674 ? 1 : 3;
            for (var k = 0; k < n; k++)
            {
                var px = x + (Rnd() - 0.5) * 10;
                var py = 182 + (n == 1 ? 0 : (k - 1) * 26) + (Rnd() - 0.5) * 10;
                var pr = (x < 100 || x > 680 ? 28 : 36) + Rnd() * 12;
                var outline = new Ellipse { Fill = Ink };
                var fill = new Ellipse { Fill = Smoke };
                outlines.Add(outline);
                fills.Add(fill);
                _puffs.Add((outline, fill, px, py, pr));
            }
        }
        // All outlines first, then all fills: only the cloud's outer edge keeps the ink line.
        foreach (var o in outlines) _sky.Children.Add(o);
        foreach (var f in fills) _sky.Children.Add(f);
    }

    private static void Place(Ellipse e, double x, double y, double r)
    {
        e.Width = e.Height = Math.Max(0, 2 * r);
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
    }

    // ---------- "FIPinator 3" and three instrument zeros, centred in the cloud ----------

    private void MakeName()
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText("FIPinator 3", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, NameSize, Ink, 1.0);
        var digitHeight = typeface.CapsHeight * NameSize;
        R = digitHeight / 2 - 3;
        var total = text.WidthIncludingTrailingWhitespace + Gap + 3 * 2 * (R + 3) + 2 * Gap;
        var left = (W - total) / 2;
        var word = new TextBlock { Text = "FIPinator 3", FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold, FontSize = NameSize, Foreground = Ink };
        Canvas.SetLeft(word, left);
        Canvas.SetTop(word, Baseline - text.Baseline);
        _name.Children.Add(word);

        // The dials are as tall as the digits: centred on the digits' middle.
        var cy = Baseline - digitHeight / 2;
        var x = left + text.WidthIncludingTrailingWhitespace + Gap + R + 3;
        Airspeed(x, cy);
        x += 2 * (R + 3) + Gap;
        Altimeter(x, cy);
        x += 2 * (R + 3) + Gap;
        Attitude(x, cy);
        _sky.Children.Add(_name);
    }

    private void Add(UIElement e) => _name.Children.Add(e);

    private void Bezel(double cx, double cy)
    {
        var outer = new Ellipse { Width = 2 * (R + 3), Height = 2 * (R + 3), Fill = Ink };
        Canvas.SetLeft(outer, cx - R - 3);
        Canvas.SetTop(outer, cy - R - 3);
        Add(outer);
        var face = new Ellipse { Width = 2 * R, Height = 2 * R, Fill = Brushes.Black };
        Canvas.SetLeft(face, cx - R);
        Canvas.SetTop(face, cy - R);
        Add(face);
    }

    private static Point At(double cx, double cy, double degrees, double r)
    {
        var a = degrees * Math.PI / 180;
        return new Point(cx + Math.Sin(a) * r, cy - Math.Cos(a) * r);
    }

    private void Ticks(double cx, double cy, int n, double from, double span)
    {
        for (var k = 0; k <= n; k++)
        {
            var big = k % 2 == 0;
            var a = from + span * k / n;
            var p1 = At(cx, cy, a, big ? R - 9 : R - 6);
            var p2 = At(cx, cy, a, R - 3);
            Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Cream, StrokeThickness = big ? 1.8 : 1.1 });
        }
    }

    private void Arc(double cx, double cy, double r, double a1, double a2, Color colour)
    {
        var s = At(cx, cy, a1, r);
        var e = At(cx, cy, a2, r);
        var figure = new PathFigure { StartPoint = s };
        figure.Segments.Add(new ArcSegment(e, new Size(r, r), 0, a2 - a1 > 180, SweepDirection.Clockwise, true));
        Add(new Path { Data = new PathGeometry([figure]), Stroke = new SolidColorBrush(colour), StrokeThickness = 3.2 });
    }

    private Line Needle(double cx, double cy, double length, double tail, double width, Brush brush, RotateTransform turn)
    {
        turn.CenterX = cx;
        turn.CenterY = cy;
        return new Line { X1 = cx, Y1 = cy + tail, X2 = cx, Y2 = cy - length, Stroke = brush, StrokeThickness = width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = turn };
    }

    private void Hub(double cx, double cy)
    {
        var hub = new Ellipse { Width = 5, Height = 5, Fill = Brushes.Gray };
        Canvas.SetLeft(hub, cx - 2.5);
        Canvas.SetTop(hub, cy - 2.5);
        Add(hub);
    }

    private void Airspeed(double cx, double cy)
    {
        Bezel(cx, cy);
        Arc(cx, cy, R - 5, -120, 40, Color.FromRgb(0x2E, 0x9E, 0x4A));
        Arc(cx, cy, R - 5, 40, 90, Color.FromRgb(0xE8, 0xC3, 0x3A));
        Arc(cx, cy, R - 5, 90, 97, Color.FromRgb(0xD2, 0x3A, 0x2A));
        Ticks(cx, cy, 10, -140, 280);
        Add(Needle(cx, cy, R - 5, 4, 2.6, Smoke, _asi));
        Hub(cx, cy);
    }

    private void Altimeter(double cx, double cy)
    {
        Bezel(cx, cy);
        Ticks(cx, cy, 10, 0, 324);
        var label = new TextBlock { Text = "ALT", FontSize = 7, Foreground = Cream, Width = 30, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(label, cx - 15);
        Canvas.SetTop(label, cy - 12);
        Add(label);
        Add(Needle(cx, cy, R - 12, 2, 3.6, Smoke, _altShort));
        Add(Needle(cx, cy, R - 4, 4, 1.7, Smoke, _altLong));
        Hub(cx, cy);
    }

    private void Attitude(double cx, double cy)
    {
        Bezel(cx, cy);
        var ball = new Canvas { Clip = new EllipseGeometry(new Point(cx, cy), R - 1, R - 1) };
        var horizon = new Canvas();
        void Rect(double y, double h, Color c)
        {
            var r = new Rectangle { Width = 4 * R, Height = h, Fill = new SolidColorBrush(c) };
            Canvas.SetLeft(r, cx - 2 * R);
            Canvas.SetTop(r, y);
            horizon.Children.Add(r);
        }
        Rect(cy - 2 * R, 2 * R, Color.FromRgb(0x3C, 0x7D, 0xD9));
        Rect(cy, 2 * R, Color.FromRgb(0x8A, 0x5A, 0x2B));
        horizon.Children.Add(new Line { X1 = cx - 2 * R, Y1 = cy, X2 = cx + 2 * R, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 1.3 });
        foreach (var d in new[] { -9.0, 9.0 })
            horizon.Children.Add(new Line { X1 = cx - 6, Y1 = cy + d, X2 = cx + 6, Y2 = cy + d, Stroke = Brushes.White, StrokeThickness = 0.9 });
        _roll.CenterX = cx;
        _roll.CenterY = cy;
        _horizon.Children.Add(_pitch);
        _horizon.Children.Add(_roll);
        horizon.RenderTransform = _horizon;
        ball.Children.Add(horizon);
        Add(ball);
        var wings = new Polyline { Stroke = Amber, StrokeThickness = 2.4 };
        foreach (var (px, py) in new[] { (-13.0, 0.0), (-4.0, 0.0), (0.0, 4.0), (4.0, 0.0), (13.0, 0.0) }) wings.Points.Add(new Point(cx + px, cy + py));
        Add(wings);
    }

    // ---------- animation ----------

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    private static double Ease(double u) => 1 - Math.Pow(1 - u, 3);
    private static double Wobble(double u) => Math.Sin(u * Math.PI * 2.5) * Math.Pow(1 - u, 2);

    private void Frame(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        Canvas.SetLeft(_plane, FlyFrom + Math.Min(t, FlySeconds) * Speed);
        Canvas.SetTop(_plane, 70 + Math.Sin(t * 3) * 2);

        foreach (var (outline, fill, x, y, r) in _puffs)
        {
            var a = Clamp01((t - (x - 8 - FlyFrom) / Speed) / 0.35);
            var size = r * (a < 1 ? a * (1.15 - 0.15 * a) : 1);
            Place(outline, x, y, size > 0 ? size + 3.5 : 0);
            Place(fill, x, y, size);
        }

        _name.Opacity = Clamp01((t - FlySeconds) / 0.4);
        var u0 = Clamp01((t - FlySeconds - 0.2) / 1.1);
        _asi.Angle = -140 + Ease(u0) * 200 + Wobble(u0) * 18;
        var u1 = Clamp01((t - FlySeconds - 0.32) / 1.3);
        _altLong.Angle = Ease(u1) * (3 * 360 + 72);
        _altShort.Angle = Ease(u1) * (0.3 * 360 + 7);
        var u2 = Clamp01((t - FlySeconds - 0.44) / 1.3);
        _roll.Angle = 64 * Wobble(u2) + (1 - Ease(u2)) * -30;
        _pitch.Y = (1 - Ease(u2)) * 10;
        _tagline.Opacity = Clamp01((t - FlySeconds - 0.9) / 0.6);

        if (t >= EndSeconds) FadeAndClose();
    }

    private void FadeAndClose()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Our own side-view drawing of a DC-6 (nose to the right) in our own paint: white top, red cheatline and tail band, bare-metal belly.</summary>
    private static Canvas Dc6()
    {
        var white = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF2));
        var metal = Frozen(Color.FromRgb(0xB9, 0xBE, 0xC3));
        var red = Frozen(Color.FromRgb(0xC8, 0x10, 0x2E));
        var edge = Frozen(Color.FromRgb(0x6E, 0x74, 0x7A));
        var dark = Frozen(Color.FromRgb(0x2B, 0x33, 0x3B));
        const string fuselage = "M10,30 C10,24 22,21 44,21 L192,21 C208,21 218,26 222,31 C218,36 208,39 192,39 L44,39 C26,39 12,36 10,30 Z";
        var plane = new Canvas { Width = 230, Height = 60 };
        void Add(string data, Brush? fill, Brush? stroke = null, Geometry? clip = null) =>
            plane.Children.Add(new Path { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke ?? edge, StrokeThickness = 1, Clip = clip });
        Add("M14,24 L6,1 L24,1 L46,22 Z", white);                         // fin
        Add("M9,12 L20,12 L28,17 L12,17 Z", red, red);                     // tail band
        Add("M4,30 L44,27 L44,33 Z", metal);                               // tailplane
        Add(fuselage, white);
        Add("M0,31 L230,31 L230,41 L0,41 Z", metal, Brushes.Transparent, Geometry.Parse(fuselage)); // bare-metal belly
        Add(fuselage, null);                                               // outline over the belly
        Add("M24,27.5 L212,27.5 L217,31 L24,31 Z", red, red);              // cheatline
        Add("M200,24 C208,24 214,27 216,29 L203,29 Z", dark, dark);        // cockpit windows
        for (var x = 60; x <= 180; x += 10)
        {
            var window = new Rectangle { Width = 4, Height = 3.5, Fill = dark };
            Canvas.SetLeft(window, x);
            Canvas.SetTop(window, 23.5);
            plane.Children.Add(window);
        }
        Add("M92,36 L152,36 L158,41 L88,41 Z", metal);                     // wing
        Add("M108,33 L128,33 C134,33 136,36 136,38 C136,40 134,43 128,43 L108,43 Z", metal); // inner engine
        Add("M134,32 L152,32 C158,32 160,35 160,37 C160,39 158,42 152,42 L134,42 Z", metal); // outer engine
        Add("M137,24 L139,24 L139,51 L137,51 Z", dark, dark);              // propellers
        Add("M161,22 L163,22 L163,52 L161,52 Z", dark, dark);
        return plane;
    }
}
