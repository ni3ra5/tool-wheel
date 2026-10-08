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

    /// Top-lit plastic, mapped over the whole wheel so every slice shares one light.
    public static readonly Brush Plastic = Frozen(new LinearGradientBrush(
        Colors.White, Color.FromRgb(0xEE, 0xEE, 0xEE), new Point(0, 0), new Point(0, Size)) { MappingMode = BrushMappingMode.Absolute });

    /// Faint dot grid, like a speaker grille pressed into the plastic.
    public static readonly Brush Dots = Frozen(new DrawingBrush(new GeometryDrawing(
        new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)), null, new EllipseGeometry(new Point(3.5, 3.5), 0.7, 0.7)))
    {
        TileMode = TileMode.Tile,
        Viewport = new Rect(0, 0, 7, 7), ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, 7, 7), ViewboxUnits = BrushMappingMode.Absolute,
    });

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// Point `r` out from the wheel's centre at `angle` (radians, clockwise from 12 o'clock).
    public static Point At(double angle, double r) => new(Size / 2 + Math.Sin(angle) * r, Size / 2 - Math.Cos(angle) * r);

    /// Slice under an offset from the centre (DIPs, y down), or null over the knob or outside the wheel.
    public static int? Slice(double dx, double dy, int count)
    {
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (count == 0 || distance <= Center || distance > Outer) return null;
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
}

/// The floating wheel: tool slices round a knob whose pointer clicks round in 10° detents toward the cursor.
sealed class WheelWindow : Window
{
    readonly Grid wheel = new() { Width = Look.Size, Height = Look.Size };
    readonly Canvas slices = new() { Width = Look.Size, Height = Look.Size };
    readonly RotateTransform knobTurn = new(0, Look.Center, Look.Center);
    readonly ScaleTransform openScale = new(1, 1, Look.Size / 2, Look.Size / 2);
    readonly Border pointer;
    readonly TextBlock label, gear;
    List<Tool> tools = new();
    double knobAngle;  // radians, unwrapped so it always turns the short way round

    public IntPtr Handle { get; private set; }
    public int? Hovered { get; private set; }
    public bool GearHovered { get; private set; }
    public Tool? HoveredTool => Hovered is int i ? tools[i] : null;

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

        // Stand-in for the Mac's frosted glass: a light disc just past the rim, its edge lit from above.
        // ponytail: no live blur; WPF transparent windows can't host Windows' acrylic. Revisit with a WinUI backdrop.
        var backdrop = new Ellipse
        {
            Width = (Look.Outer + Look.Gap) * 2,
            Height = (Look.Outer + Look.Gap) * 2,
            Fill = new SolidColorBrush(Color.FromRgb(0xDB, 0xDB, 0xDB)),
            Stroke = new LinearGradientBrush(Color.FromArgb(242, 255, 255, 255), Color.FromArgb(13, 255, 255, 255), 90),
            StrokeThickness = 1,
        };

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
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x4D)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        gear = new TextBlock
        {
            Text = "",  // Settings glyph
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Look.GrayBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Look.GearOffset * 2, 0, 0),
        };

        var body = new Grid { Effect = new DropShadowEffect { Direction = 270, ShadowDepth = 5, BlurRadius = 14, Opacity = 0.16 } };
        body.Children.Add(slices);
        body.Children.Add(knob);
        wheel.Children.Add(backdrop);
        wheel.Children.Add(body);
        wheel.Children.Add(label);
        wheel.Children.Add(gear);
        wheel.RenderTransform = openScale;
        Content = wheel;

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.MakeNonActivating(Handle);
        };
    }

    public void Load(List<Tool> tools)
    {
        this.tools = tools;
        Hovered = null;
        GearHovered = false;
        slices.Children.Clear();
        double step = 2 * Math.PI / Math.Max(tools.Count, 1);
        for (int i = 0; i < tools.Count; i++)
        {
            double a = i * step;
            var geometry = Look.Wedge(a - step / 2, a + step / 2);
            var slice = new Canvas { Width = Look.Size, Height = Look.Size, RenderTransform = new TranslateTransform() };
            slice.Children.Add(new Path { Data = geometry, Fill = Look.Plastic, Stroke = Look.Plastic, StrokeThickness = Look.Corner * 2, StrokeLineJoin = PenLineJoin.Round });
            slice.Children.Add(new Path { Data = geometry, Fill = Look.Dots });

            var tool = tools[i];
            FrameworkElement face = tool.Expanded is { } path && Native.Icon(path) is { } icon
                ? new Image { Source = icon }
                : new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 26, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x47, 0x47)) };
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
            Place(slice, face, Look.At(a, Look.IconRadius), 44);
            if (tool.IsRunning)
                Place(slice, new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3)) }, Look.At(a, Look.RunningDot), 3.5);
            slices.Children.Add(slice);
        }
        Restyle();
    }

    static void Place(Canvas canvas, FrameworkElement element, Point center, double size)
    {
        element.Width = element.Height = size;
        Canvas.SetLeft(element, center.X - size / 2);
        Canvas.SetTop(element, center.Y - size / 2);
        canvas.Children.Add(element);
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

    void Restyle()
    {
        double step = 2 * Math.PI / Math.Max(tools.Count, 1);
        for (int i = 0; i < slices.Children.Count; i++)
        {
            var slice = (Canvas)slices.Children[i];
            bool on = Hovered == i;
            Lift(slice, i * step, on ? 3 : 0);  // hovered slices nudge outward along their slice
            slice.Effect = on ? new DropShadowEffect { Direction = 270, ShadowDepth = 5, BlurRadius = 16, Opacity = 0.18 } : null;
            Panel.SetZIndex(slice, on ? 1 : 0);
        }
        pointer.BeginAnimation(OpacityProperty, new DoubleAnimation(Hovered is null ? 0 : 1, TimeSpan.FromMilliseconds(150)));
        label.Text = GearHovered ? "Settings" : HoveredTool?.Name ?? "";
        gear.Foreground = GearHovered ? Look.AccentBrush : Look.GrayBrush;
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
        if (Hovered is int i) Lift((Canvas)slices.Children[i], i * 2 * Math.PI / tools.Count, -1);
    }

    public void PlayOpen()
    {
        var grow = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        openScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        openScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }
}
