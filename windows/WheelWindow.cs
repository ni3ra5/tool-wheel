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
        GearOffset = 26, GearHit = 14, RunningDot = 82, IconRadius = (Center + Outer) / 2 + 4, BackdropInset = 8;
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
        if (end - start <= 2 * Math.Asin(half / inner)) return Geometry.Empty;  // opening or closing: too thin to draw
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

    public static readonly TimeSpan Spring = TimeSpan.FromMilliseconds(350);

    /// Fades an element in where it stands, growing it from a little smaller unless `grow` is off.
    public static void Appear(UIElement element, bool grow = true, double fadeMs = 250)
    {
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeMs)));
        if (!grow) return;
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var up = new DoubleAnimation(0.6, 1, Spring) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    /// Fades an element out (shrinking it unless `grow` is off), then calls `gone`. Ease-in keeps it solid at first;
    /// ease-out clears it quickly, for something others are about to slide over.
    public static void Vanish(UIElement element, Action gone, bool grow = true, EasingMode ease = EasingMode.EaseIn)
    {
        element.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, ease == EasingMode.EaseIn ? Spring : TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = ease } };
        fade.Completed += (_, _) => gone();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (!grow) return;
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var down = new DoubleAnimation(0.6, fade.Duration) { EasingFunction = fade.EasingFunction };  // shrinks as it fades, like the Mac
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }
}

/// A place on the wheel: an angle, a span (a slice's width) and a lift outward. Animates, and calls `Place` as it
/// changes, so slices, icons and Settings' controls glide round, open and close when tools are added, removed
/// or reordered.
sealed class Polar : Animatable
{
    public static readonly DependencyProperty AngleProperty = Register(nameof(Angle));
    public static readonly DependencyProperty SpanProperty = Register(nameof(Span));
    public static readonly DependencyProperty LiftProperty = Register(nameof(Lift));
    static DependencyProperty Register(string name) => DependencyProperty.Register(name, typeof(double), typeof(Polar), new(0.0, Moved));
    public double Angle => (double)GetValue(AngleProperty);
    public double Span => (double)GetValue(SpanProperty);
    public double Lift => (double)GetValue(LiftProperty);
    public Action? Place;

    static void Moved(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((Polar)d).Place?.Invoke();
    protected override Freezable CreateInstanceCore() => new Polar();

    public void Jump(double angle, double span = 0)
    {
        BeginAnimation(AngleProperty, null);
        BeginAnimation(SpanProperty, null);
        SetValue(AngleProperty, angle);
        SetValue(SpanProperty, span);
        Place?.Invoke();
    }

    /// Glides the short way round, with a little spring.
    public void Glide(double angle, double span = 0)
    {
        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        BeginAnimation(AngleProperty, new DoubleAnimation(Angle + Math.IEEERemainder(angle - Angle, 2 * Math.PI), Look.Spring) { EasingFunction = ease });
        BeginAnimation(SpanProperty, new DoubleAnimation(span, Look.Spring) { EasingFunction = ease });
    }

    public void LiftTo(double lift) =>
        BeginAnimation(LiftProperty, new DoubleAnimation(lift, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
}

/// The wheel itself: tool slices round a knob whose pointer clicks round in 10° detents toward the cursor.
/// Settings shows it in edit mode: static, no backdrop, "Remove" in place of the gear.
sealed class WheelView : Grid
{
    /// One tool: its slice, icon and "open" dot, which move together.
    sealed class Entry
    {
        public readonly string Key;
        public readonly Polar Polar = new();
        public readonly Canvas Slice = new() { Width = Look.Size, Height = Look.Size };
        public readonly FrameworkElement Icon;
        public readonly Ellipse Dot = new() { Width = 3.5, Height = 3.5, Fill = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3)) };
        readonly Path plastic = new() { Fill = Look.Plastic, Stroke = Look.Plastic, StrokeThickness = Look.Corner * 2, StrokeLineJoin = PenLineJoin.Round };
        readonly Path dots = new() { Fill = Look.Dots };
        readonly TranslateTransform lift = new();
        double shapedAngle = double.NaN, shapedSpan = double.NaN;

        public Entry(Tool tool)
        {
            Key = KeyOf(tool);
            Slice.Children.Add(plastic);
            Slice.Children.Add(dots);
            Slice.RenderTransform = lift;
            Icon = tool.Expanded is { } path && Native.Icon(path, 128) is { } icon
                ? new Image { Source = icon }
                : new TextBlock { Text = "", FontFamily = Look.Glyphs, FontSize = 26, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x47, 0x47)) };
            Icon.Width = Icon.Height = 44;
            RenderOptions.SetBitmapScalingMode(Icon, BitmapScalingMode.HighQuality);
            Polar.Place = Place;
        }

        void Place()
        {
            double angle = Polar.Angle, span = Polar.Span, up = Polar.Lift;
            if (angle != shapedAngle || span != shapedSpan)  // reshape only when it moves round, not when it lifts
            {
                shapedAngle = angle;
                shapedSpan = span;
                plastic.Data = dots.Data = Look.Wedge(angle - span / 2, angle + span / 2);
            }
            lift.X = Math.Sin(angle) * up;  // hovered slices nudge outward along their slice
            lift.Y = -Math.Cos(angle) * up;
            Look.Place(Icon, Look.At(angle, Look.IconRadius + up));
            Look.Place(Dot, Look.At(angle, Look.RunningDot + up));
        }
    }

    public static string KeyOf(Tool tool) => tool.Expanded ?? tool.Name;

    readonly bool editing;
    readonly Grid body;
    readonly Canvas slices = new() { Width = Look.Size, Height = Look.Size };
    readonly Canvas faces = new() { Width = Look.Size, Height = Look.Size };
    readonly RotateTransform knobTurn = new(0, Look.Center, Look.Center);
    readonly ScaleTransform openScale = new(1, 1, Look.Size / 2, Look.Size / 2);
    readonly Border pointer;
    readonly TextBlock label, gear;
    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    List<Tool> tools = new();
    List<Entry> order = new();
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
            // Stand-in for the Mac's frosted glass: a light disc that stops inside the rim, so it fills the gaps
            // between slices but never shows past the edge.
            // ponytail: no live blur; WPF transparent windows can't host Windows' acrylic. Revisit with a WinUI backdrop.
            Children.Add(new Ellipse
            {
                Width = (Look.Outer - Look.BackdropInset) * 2,
                Height = (Look.Outer - Look.BackdropInset) * 2,
                Fill = new SolidColorBrush(Color.FromRgb(0xDB, 0xDB, 0xDB)),
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

        double step = 2 * Math.PI / Math.Max(tools.Count, 1);
        var next = new List<Entry>();
        for (int i = 0; i < tools.Count; i++)
        {
            bool known = entries.TryGetValue(KeyOf(tools[i]), out var entry);
            if (!known)
            {
                entry = new Entry(tools[i]);
                entries[entry.Key] = entry;
                slices.Children.Add(entry.Slice);
                faces.Children.Add(entry.Icon);
                faces.Children.Add(entry.Dot);
            }
            if (!glide) entry!.Polar.Jump(i * step, step);
            else if (known) entry!.Polar.Glide(i * step, step);
            else
            {
                // A new tool: its slice opens in the gap while the others make room, and its icon fades in.
                entry!.Polar.Jump(i * step, 0);
                entry.Polar.Glide(i * step, step);
                Look.Appear(entry.Slice, grow: false, fadeMs: 60);  // its widening is the entrance; a slow fade reads as a grey wedge
                Look.Appear(entry.Icon);
                Look.Appear(entry.Dot);
            }
            entry.Dot.Visibility = running is not null && i < running.Count && running[i] ? Visibility.Visible : Visibility.Collapsed;
            next.Add(entry);
        }
        foreach (var gone in order.Except(next).ToList())
        {
            entries.Remove(gone.Key);
            void Drop()
            {
                slices.Children.Remove(gone.Slice);
                faces.Children.Remove(gone.Icon);
                faces.Children.Remove(gone.Dot);
            }
            if (!glide) { Drop(); continue; }
            // A removed tool: its slice closes up where it was while the others slide together.
            gone.Polar.Glide(gone.Polar.Angle, 0);
            Look.Vanish(gone.Slice, Drop, grow: false);
            Panel.SetZIndex(gone.Icon, -1);  // under the neighbour sliding into its place
            Look.Vanish(gone.Icon, () => { }, ease: EasingMode.EaseOut);
            Look.Vanish(gone.Dot, () => { }, ease: EasingMode.EaseOut);
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
        for (int i = 0; i < order.Count; i++)
        {
            bool on = Hovered == i;
            order[i].Polar.LiftTo(on ? 3 : 0);
            order[i].Slice.Effect = on ? new DropShadowEffect { Direction = 270, ShadowDepth = 5, BlurRadius = 16, Opacity = 0.18 } : null;
            Panel.SetZIndex(order[i].Slice, on ? 1 : 0);
        }
        pointer.BeginAnimation(OpacityProperty, new DoubleAnimation(Hovered is null ? 0 : 1, TimeSpan.FromMilliseconds(150)));
        int? named = editing ? TrashHovered ?? Hovered : Hovered;
        label.Text = GearHovered ? "Settings" : named is int n ? tools[n].Name : "";
        if (editing) gear.Opacity = TrashHovered is null ? 0 : 1;
        else gear.Foreground = GearHovered ? Look.AccentBrush : Look.GrayBrush;
    }

    /// The hovered slice sinks a touch, like a key going down.
    public void Press()
    {
        if (Hovered is int i) order[i].Polar.LiftTo(-1);
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
