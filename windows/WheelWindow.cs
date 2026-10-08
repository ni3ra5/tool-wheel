using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ToolWheel;

/// Geometry and colours, the same numbers as the Mac version (mac/Sources/ToolWheel/main.swift).
static class Look
{
    public const double Size = 400, Outer = 172, Center = 56, Gap = 2, KnobGap = 1.5, Corner = 6, DialRim = 5,
        GearOffset = 26, GearHit = 14, RunningDot = 82, IconRadius = (Center + Outer) / 2 + 4;
    public const double Detent = 2 * Math.PI / 36;  // 10° per click

    public static readonly Color Accent = Color.FromRgb(0xFF, 0x3C, 0x00);  // #FF3C00
    static readonly Color Gray = Color.FromRgb(0x99, 0x99, 0x99);
    public static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Accent));
    public static readonly Brush GrayBrush = Frozen(new SolidColorBrush(Gray));
    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// Top-lit plastic, mapped over the whole wheel so every slice shares one light.
    public static readonly Brush Plastic = Frozen(new LinearGradientBrush(
        Colors.White, Color.FromRgb(0xEE, 0xEE, 0xEE), new Point(0, 0), new Point(0, Size)) { MappingMode = BrushMappingMode.Absolute });

    /// Faint dot grid, like a speaker grille pressed into the plastic.
    public static readonly Brush Dots = DotGrid(Color.FromArgb(15, 0, 0, 0), 7);

    public static Brush DotGrid(Color color, double step) => Frozen(new DrawingBrush(new GeometryDrawing(
        new SolidColorBrush(color), null, new EllipseGeometry(new Point(step / 2, step / 2), 0.7, 0.7)))
    {
        TileMode = TileMode.Tile,
        Viewport = new Rect(0, 0, step, step), ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, step, step), ViewboxUnits = BrushMappingMode.Absolute,
    });

    public static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// Point `r` out from the wheel's centre at `angle` (radians, clockwise from 12 o'clock).
    public static Point At(double angle, double r) => new(Size / 2 + Math.Sin(angle) * r, Size / 2 - Math.Cos(angle) * r);

    /// Slice under an offset from the centre (DIPs, y down), or null over the knob or beyond `outer`.
    public static int? Slice(double dx, double dy, int count, double outer = Outer)
    {
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (count == 0 || distance <= Center || distance > outer) return null;
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        return (int)Math.Round(angle / (2 * Math.PI / count)) % count;
    }

    static IEnumerable<Point> Arc(double r, double from, double to)
    {
        for (int i = 0; i <= 32; i++) yield return At(from + (to - from) * i / 32, r);
    }

    /// Ring segment whose edges run parallel to its radial lines, so every gap is the same width. Drawn shrunk by
    /// `Corner`; a round-joined stroke of twice that grows it back with rounded corners.
    public static Geometry Wedge(double start, double end)
    {
        double inner = Center + KnobGap + Corner, outer = Outer - Corner, half = Gap / 2 + Corner;
        var points = Arc(outer, start + Math.Asin(half / outer), end - Math.Asin(half / outer))
            .Concat(Arc(inner, end - Math.Asin(half / inner), start + Math.Asin(half / inner))).ToList();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// Centres an element on a point of its canvas.
    public static void Place(FrameworkElement element, Point at)
    {
        Canvas.SetLeft(element, at.X - element.Width / 2);
        Canvas.SetTop(element, at.Y - element.Height / 2);
    }
}

/// A position round the wheel, an angle plus a lift outward, that animates and calls `Place` as it changes.
/// Lets icons and Settings' controls glide along the arc when tools are reordered.
sealed class Polar : Animatable
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double), typeof(Polar), new(0.0, Moved));
    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(nameof(Lift), typeof(double), typeof(Polar), new(0.0, Moved));
    public double Angle => (double)GetValue(AngleProperty);
    public double Lift => (double)GetValue(LiftProperty);
    public Action<double, double>? Place;  // (angle, lift)

    static void Moved(DependencyObject d, DependencyPropertyChangedEventArgs _) { var p = (Polar)d; p.Place?.Invoke(p.Angle, p.Lift); }
    protected override Freezable CreateInstanceCore() => new Polar();

    public void Jump(double angle)
    {
        BeginAnimation(AngleProperty, null);
        SetValue(AngleProperty, angle);
        Place?.Invoke(Angle, Lift);
    }

    /// Glides the short way round, with a little spring.
    public void Glide(double angle)
    {
        double target = Angle + Math.IEEERemainder(angle - Angle, 2 * Math.PI);
        BeginAnimation(AngleProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(350)) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } });
    }

    public void LiftTo(double lift) =>
        BeginAnimation(LiftProperty, new DoubleAnimation(lift, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
}

/// The wheel itself: tool slices round a knob whose pointer clicks round in 10° detents toward the cursor.
/// Settings shows it in edit mode: static, no backdrop, "Remove" in place of the gear.
sealed class WheelView : Grid
{
    /// One tool's icon and "open" dot, which travel together when the wheel is reordered.
    sealed class Face
    {
        public readonly Polar Polar = new();
        public readonly FrameworkElement Icon;
        public readonly Ellipse Dot = new() { Width = 3.5, Height = 3.5, Fill = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3)) };

        public Face(Tool tool)
        {
            Icon = tool.Expanded is { } path && Native.Icon(path, 128) is { } icon
                ? new Image { Source = icon }
                : new TextBlock { Text = "", FontFamily = Look.Glyphs, FontSize = 26, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x47, 0x47)) };
            Icon.Width = Icon.Height = 44;
            RenderOptions.SetBitmapScalingMode(Icon, BitmapScalingMode.HighQuality);
            Polar.Place = (angle, lift) =>
            {
                Look.Place(Icon, Look.At(angle, Look.IconRadius + lift));
                Look.Place(Dot, Look.At(angle, Look.RunningDot + lift));
            };
        }
    }

    readonly bool editing;
    readonly Grid body;
    readonly Canvas slices = new() { Width = Look.Size, Height = Look.Size };
    readonly Canvas faces = new() { Width = Look.Size, Height = Look.Size };
    readonly RotateTransform knobTurn = new(0, Look.Center, Look.Center);
    readonly ScaleTransform openScale = new(1, 1, Look.Size / 2, Look.Size / 2);
    readonly Border pointer;
    readonly TextBlock label, gear;
    readonly Dictionary<string, Face> facesByTool = new(StringComparer.OrdinalIgnoreCase);
    List<Tool> tools = new();
    List<Face> order = new();
    double knobAngle;  // radians, unwrapped so it always turns the short way round

    public int? Hovered { get; private set; }
    public int? TrashHovered { get; private set; }  // editing only
    public bool GearHovered { get; private set; }
    public Tool? HoveredTool => Hovered is int i ? tools[i] : null;
    public IReadOnlyList<Tool> Tools => tools;

    public WheelView(bool editing = false)
    {
        this.editing = editing;
        Width = Height = Look.Size;

        // Knob: outer ring and slightly smaller face, top-lit, casting its shadow straight down.
        var knob = new Grid { Width = Look.Center * 2, Height = Look.Center * 2 };
        knob.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Colors.White, Color.FromRgb(0xE0, 0xE0, 0xE0), 90) });
        knob.Children.Add(new Ellipse
        {
            Margin = new Thickness(Look.DialRim),
            Fill = new LinearGradientBrush(Color.FromRgb(0xFE, 0xFE, 0xFE), Color.FromRgb(0xED, 0xED, 0xED), 90),
            Stroke = new LinearGradientBrush(Color.FromRgb(0xD1, 0xD1, 0xD1), Colors.White, 90),
            StrokeThickness = 0.75,
        });
        var turning = new Canvas { Width = Look.Center * 2, Height = Look.Center * 2, RenderTransform = knobTurn };
        for (int i = 0; i < 72; i++)  // engraved scale: a tick every 5°, a longer one every 30°
        {
            double a = i * 2 * Math.PI / 72, r0 = Look.Center - Look.DialRim - 3;
            bool major = i % 6 == 0;
            double r1 = r0 - (major ? 5 : 3);
            turning.Children.Add(new Line
            {
                X1 = Look.Center + Math.Sin(a) * r0, Y1 = Look.Center - Math.Cos(a) * r0,
                X2 = Look.Center + Math.Sin(a) * r1, Y2 = Look.Center - Math.Cos(a) * r1,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(major ? 41 : 23), 0, 0, 0)),
                StrokeThickness = major ? 0.9 : 0.6,
            });
        }
        pointer = new Border
        {
            Width = 2.5, Height = 9, CornerRadius = new CornerRadius(1.25),
            Background = Look.AccentBrush,
            Effect = new DropShadowEffect { Color = Look.Accent, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.6 },
            Opacity = 0,
        };
        Canvas.SetLeft(pointer, Look.Center - 1.25);
        Canvas.SetTop(pointer, 1.5);  // spans the rim, from just inside the edge
        turning.Children.Add(pointer);
        knob.Children.Add(turning);
        knob.Effect = new DropShadowEffect { Color = Color.FromRgb(0x33, 0x40, 0x47), Direction = 270, ShadowDepth = 7, BlurRadius = 18, Opacity = 0.42 };

        label = new TextBlock
        {
            Width = Look.Center * 1.4,
            MaxHeight = 32,  // two lines, then "…"
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = Look.Text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x4D)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        gear = editing
            ? new TextBlock { Text = "Remove", FontFamily = Look.Text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Look.AccentBrush, Opacity = 0 }
            : new TextBlock { Text = "", FontFamily = Look.Glyphs, FontSize = 13, Foreground = Look.GrayBrush };  // Settings glyph
        gear.HorizontalAlignment = HorizontalAlignment.Center;
        gear.VerticalAlignment = VerticalAlignment.Center;
        gear.Margin = new Thickness(0, Look.GearOffset * 2, 0, 0);

        body = new Grid { Effect = new DropShadowEffect { Direction = 270, ShadowDepth = 5, BlurRadius = 14, Opacity = 0.16 } };
        body.Children.Add(slices);
        body.Children.Add(faces);  // above a lifted slice
        body.Children.Add(knob);
        if (!editing)
        {
            // Stand-in for the Mac's frosted glass: a light disc just past the rim, its edge lit from above.
            // ponytail: no live blur; WPF transparent windows can't host Windows' acrylic. Revisit with a WinUI backdrop.
            Children.Add(new Ellipse
            {
                Width = (Look.Outer + Look.Gap) * 2,
                Height = (Look.Outer + Look.Gap) * 2,
                Fill = new SolidColorBrush(Color.FromRgb(0xDB, 0xDB, 0xDB)),
                Stroke = new LinearGradientBrush(Color.FromArgb(242, 255, 255, 255), Color.FromArgb(13, 255, 255, 255), 90),
                StrokeThickness = 1,
            });
        }
        Children.Add(body);
        Children.Add(label);
        Children.Add(gear);
        RenderTransform = openScale;
    }

    /// Lays out `tools`. With `glide`, icons already on the wheel travel to their new places.
    public void Load(List<Tool> tools, IReadOnlyList<bool>? running = null, bool glide = false)
    {
        this.tools = tools;
        Hovered = TrashHovered = null;
        GearHovered = false;

        slices.Children.Clear();
        double step = 2 * Math.PI / Math.Max(tools.Count, 1);
        for (int i = 0; i < tools.Count; i++)
        {
            var geometry = Look.Wedge(i * step - step / 2, i * step + step / 2);
            var slice = new Canvas { Width = Look.Size, Height = Look.Size, RenderTransform = new TranslateTransform() };
            slice.Children.Add(new Path { Data = geometry, Fill = Look.Plastic, Stroke = Look.Plastic, StrokeThickness = Look.Corner * 2, StrokeLineJoin = PenLineJoin.Round });
            slice.Children.Add(new Path { Data = geometry, Fill = Look.Dots });
            slices.Children.Add(slice);
        }

        var next = new List<Face>();
        for (int i = 0; i < tools.Count; i++)
        {
            string key = tools[i].Expanded ?? tools[i].Name;
            bool known = facesByTool.TryGetValue(key, out var face);
            if (!known)
            {
                face = new Face(tools[i]);
                facesByTool[key] = face;
                faces.Children.Add(face.Icon);
                faces.Children.Add(face.Dot);
                if (glide) face.Icon.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            if (glide && known) face!.Polar.Glide(i * step);
            else face!.Polar.Jump(i * step);
            face.Polar.LiftTo(0);
            face.Dot.Visibility = running is not null && i < running.Count && running[i] ? Visibility.Visible : Visibility.Collapsed;
            next.Add(face);
        }
        foreach (var gone in order.Except(next))
        {
            faces.Children.Remove(gone.Icon);
            faces.Children.Remove(gone.Dot);
            facesByTool.Remove(facesByTool.First(kv => kv.Value == gone).Key);
        }
        order = next;
        Restyle();
    }

    /// Settings: which tool's grip and trash the cursor is on.
    public void SetHover(int? hovered, int? trash)
    {
        if (hovered == Hovered && trash == TrashHovered) return;
        Hovered = hovered;
        TrashHovered = trash;
        Restyle();
    }

    /// dx/dy: cursor offset from the wheel's centre in DIPs (y down). True when the knob clicks over a detent.
    public bool Track(double dx, double dy)
    {
        var hovered = Look.Slice(dx, dy, tools.Count);
        bool gearHovered = Math.Sqrt(dx * dx + (dy - Look.GearOffset) * (dy - Look.GearOffset)) < Look.GearHit;
        if (hovered != Hovered || gearHovered != GearHovered)
        {
            Hovered = hovered;
            GearHovered = gearHovered;
            Restyle();
        }
        if (hovered is null) return false;

        double toward = knobAngle + Math.IEEERemainder(Math.Atan2(dx, -dy) - knobAngle, 2 * Math.PI);
        double snapped = Math.Round(toward / Look.Detent) * Look.Detent;
        if (Math.Abs(snapped - knobAngle) <= Look.Detent / 2) return false;
        knobAngle = snapped;
        knobTurn.Angle = knobAngle * 180 / Math.PI;  // no animation: each detent lands with a click
        return true;
    }

    /// Turns the knob to face slice `i`, the short way round (Settings, where there's no cursor to follow).
    public void PointAt(int i)
    {
        knobAngle += Math.IEEERemainder(i * 2 * Math.PI / Math.Max(tools.Count, 1) - knobAngle, 2 * Math.PI);
        knobTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(knobAngle * 180 / Math.PI, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void Restyle()
    {
        double step = 2 * Math.PI / Math.Max(tools.Count, 1);
        for (int i = 0; i < slices.Children.Count; i++)
        {
            var slice = (Canvas)slices.Children[i];
            bool on = Hovered == i;
            double lift = on ? 3 : 0;  // hovered slices nudge outward along their slice
            Lift(slice, i * step, lift);
            order[i].Polar.LiftTo(lift);
            slice.Effect = on ? new DropShadowEffect { Direction = 270, ShadowDepth = 5, BlurRadius = 16, Opacity = 0.18 } : null;
            Panel.SetZIndex(slice, on ? 1 : 0);
        }
        pointer.BeginAnimation(OpacityProperty, new DoubleAnimation(Hovered is null ? 0 : 1, TimeSpan.FromMilliseconds(150)));
        int? named = editing ? TrashHovered ?? Hovered : Hovered;
        label.Text = GearHovered ? "Settings" : named is int n ? tools[n].Name : "";
        if (editing) gear.Opacity = TrashHovered is null ? 0 : 1;
        else gear.Foreground = GearHovered ? Look.AccentBrush : Look.GrayBrush;
    }

    static void Lift(Canvas slice, double angle, double distance)
    {
        var move = (TranslateTransform)slice.RenderTransform;
        var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Math.Sin(angle) * distance, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Math.Cos(angle) * distance, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    /// The hovered slice sinks a touch, like a key going down.
    public void Press()
    {
        if (Hovered is not int i) return;
        Lift((Canvas)slices.Children[i], i * 2 * Math.PI / tools.Count, -1);
        order[i].Polar.LiftTo(-1);
    }

    public void PlayOpen()
    {
        var grow = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        openScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        openScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }
}

/// The floating, click-through-to-nothing panel that holds the wheel at the cursor.
sealed class WheelWindow : Window
{
    public readonly WheelView View = new();
    public IntPtr Handle { get; private set; }

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Look.Size;
        Content = View;

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.MakeNonActivating(Handle);
        };
    }
}
