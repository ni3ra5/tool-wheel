using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ToolWheel;

/// Settings, ported from the Mac (mac/Sources/ToolWheel/Settings.swift): dark, flat, dotted. The wheel on the left with
/// a pill outside each slice (trash to remove, grip to drag round), installed apps on the right, and along the
/// bottom open-at-login, the shortcut and the open mode. Every change is saved to tools.json straight away.
sealed class SettingsWindow : Window
{
    /// While the shortcut is being recorded, holding it mustn't open the wheel.
    public static bool Recording { get; private set; }

    static readonly Color Surface = Color.FromRgb(0x18, 0x18, 0x18);
    static Brush White(double opacity) => Look.Frozen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 255, 255, 255)));

    const double EditorSize = Look.Size + 84;  // room for the controls ring outside the wheel
    const double PillWidth = 58, PillHeight = 26;

    readonly Config config;
    readonly Action<Config> changed;
    readonly WheelView preview = new(editing: true);
    readonly Canvas pills = new() { Width = EditorSize, Height = EditorSize };
    readonly Dictionary<string, Pill> pillsByTool = new(StringComparer.OrdinalIgnoreCase);
    readonly TextBlock emptyHint;
    readonly StackPanel list = new();
    readonly TextBlock noMatches;
    readonly TextBox search;
    readonly List<Row> rows = new();
    string? dragging;  // key of the tool being dragged round

    public IntPtr Handle { get; private set; }

    public SettingsWindow(Config config, Action<Config> changed)
    {
        this.config = config;
        this.changed = changed;
        Title = "Tool Wheel Settings";
        ResizeMode = ResizeMode.CanMinimize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;  // on the screen with the cursor
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = new SolidColorBrush(Surface);

        // ---- Left: the wheel, its controls, and the switches along the bottom ----
        var editor = new Grid { Width = EditorSize, Height = EditorSize, Margin = new Thickness(0, -36, 0, 0) };  // clear of the bottom row
        preview.IsHitTestVisible = false;  // the wheel is just a picture; the pills do the work
        editor.Children.Add(preview);
        editor.Children.Add(pills);
        emptyHint = new TextBlock
        {
            Text = "Add tools from the list", FontFamily = Look.Text, FontSize = 11, Foreground = White(0.35),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, (Look.Outer + 24) * 2, 0, 0), IsHitTestVisible = false,
        };
        editor.Children.Add(emptyHint);

        var left = new Grid { Width = 529, Height = 580 };
        left.Children.Add(new Grid { Children = { editor }, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(Corner(LoginSwitch(), HorizontalAlignment.Left));
        left.Children.Add(Corner(new StackPanel { Children = { ShortcutField(), OpenMode() } }, HorizontalAlignment.Right));

        // ---- Right: search the installed apps; click one to add or remove it ----
        search = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = White(0.9), CaretBrush = White(0.9),
            FontFamily = Look.Text, FontSize = 13, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center,
        };
        var placeholder = new TextBlock { Text = "Search apps and tools", FontFamily = Look.Text, FontSize = 13, Foreground = White(0.35), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text == "" ? Visibility.Visible : Visibility.Collapsed; Filter(); };
        var searchBox = new Border
        {
            Background = White(0.06), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 14, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new TextBlock { Text = "", FontFamily = Look.Glyphs, FontSize = 12, Foreground = White(0.35), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, Dock.Left),
                    new Grid { Children = { placeholder, search } },
                },
            },
        };
        noMatches = new TextBlock { Text = "Loading apps…", FontFamily = Look.Text, FontSize = 12, Foreground = White(0.35), Margin = new Thickness(10) };
        list.Children.Add(new TextBlock
        {
            Text = "INSTALLED APPS", FontFamily = Look.Text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = White(0.35),
            Margin = new Thickness(10, 10, 10, 4),
        });
        list.Children.Add(noMatches);
        var scroll = new ScrollViewer
        {
            Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(-10, 14, -10, 0),
            Focusable = false,
        };
        var right = new DockPanel { Width = 330 - 48, Margin = new Thickness(24, 20, 24, 20), LastChildFill = true };
        right.Children.Add(Docked(new TextBlock { Text = "Add tools", FontFamily = Look.Text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = White(0.9) }, Dock.Top));
        right.Children.Add(Docked(searchBox, Dock.Top));
        right.Children.Add(scroll);

        var root = new Grid
        {
            Width = 860, Height = 580,
            Background = Look.DotGrid(Color.FromArgb(18, 255, 255, 255), 14),
            ColumnDefinitions = { new() { Width = new GridLength(529) }, new() { Width = new GridLength(1) }, new() { Width = new GridLength(330) } },
        };
        root.Children.Add(left);
        var divider = new Border { Background = White(0.06) };
        Grid.SetColumn(divider, 1);
        root.Children.Add(divider);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        root.Children.Add(new Border { Height = 1, Background = White(0.08), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });  // under the title bar
        Grid.SetColumnSpan(root.Children[^1], 3);
        Content = new Border { Background = new SolidColorBrush(Surface), Child = root };

        LoadWheel(glide: false);
        Loaded += (_, _) => LoadApps();  // once the dispatcher is running, so the await comes back to this thread

        PreviewKeyDown += (_, e) =>
        {
            if (!Recording) return;
            if (e.Key == Key.Escape) StopRecording();
            e.Handled = true;  // don't let keys leak into the search field
        };
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.DarkTitleBar(Handle, Surface, Color.FromRgb(0x99, 0x99, 0x99));
        };
    }

    static T Docked<T>(T element, Dock dock) where T : UIElement { DockPanel.SetDock(element, dock); return element; }

    static FrameworkElement Corner(FrameworkElement content, HorizontalAlignment side)
    {
        content.HorizontalAlignment = side;
        content.VerticalAlignment = VerticalAlignment.Bottom;
        content.Margin = new Thickness(20);
        return content;
    }

    void Save()
    {
        Store.Save(config);
        changed(config);
    }

    // ---- The wheel editor ----

    /// Six-dot drag handle.
    static UniformGrid Grip() => new()
    {
        Columns = 2, Rows = 3, Width = 8.5, Height = 14,
        Children = { Dot(), Dot(), Dot(), Dot(), Dot(), Dot() },
    };
    static Ellipse Dot() => new() { Width = 3, Height = 3, Fill = Brushes.White };

    /// Trash and grip in a capsule just past a slice's rim.
    sealed class Pill : Border
    {
        public readonly Tool Tool;
        public readonly TextBlock Trash = new() { Text = "", FontFamily = Look.Glyphs, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
        public readonly Border Handle;
        public readonly UniformGrid Dots = SettingsWindow.Grip();
        public readonly Polar Polar = new();

        public Pill(Tool tool)
        {
            Tool = tool;
            Width = PillWidth;
            Height = PillHeight;
            CornerRadius = new CornerRadius(PillHeight / 2);
            Handle = new Border { Child = Dots, Padding = new Thickness(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Trash, new Border { Width = 9 }, Handle },
            };
            Trash.ToolTip = $"Remove {tool.Name}";
            Polar.Place = () => Look.Place(this, new Point(EditorSize / 2 + Math.Sin(Polar.Angle) * Radius(Polar.Angle), EditorSize / 2 - Math.Cos(Polar.Angle) * Radius(Polar.Angle)));
        }

        /// Pills are wider than tall, so at 3 and 9 o'clock they reach further toward the wheel; push those out
        /// so the clearance from the rim is the same all round.
        static double Radius(double angle) => Look.Outer + 10 + PillHeight / 2 + Math.Abs(Math.Sin(angle)) * (PillWidth - PillHeight) / 2;
    }

    int IndexOf(Tool tool) => config.Wheel.FindIndex(t => t.Same(tool));
    static string KeyOf(Tool tool) => WheelView.KeyOf(tool);

    void LoadWheel(bool glide)
    {
        preview.Load(config.Wheel, Apps.Running(config.Wheel), glide);
        double step = 2 * Math.PI / Math.Max(config.Wheel.Count, 1);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < config.Wheel.Count; i++)
        {
            var tool = config.Wheel[i];
            keep.Add(KeyOf(tool));
            if (pillsByTool.TryGetValue(KeyOf(tool), out var pill))
            {
                if (glide) pill.Polar.Glide(i * step);
                else pill.Polar.Jump(i * step);
            }
            else
            {
                pill = AddPill(tool);
                pill.Polar.Jump(i * step);
                if (glide) Look.Appear(pill);
            }
        }
        foreach (var gone in pillsByTool.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            var pill = pillsByTool[gone];
            pillsByTool.Remove(gone);
            if (glide) Look.Vanish(pill, () => pills.Children.Remove(pill));
            else pills.Children.Remove(pill);
        }
        emptyHint.Visibility = config.Wheel.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestylePills();
    }

    Pill AddPill(Tool tool)
    {
        var pill = new Pill(tool);
        pillsByTool[KeyOf(tool)] = pill;
        pills.Children.Add(pill);

        pill.Trash.MouseEnter += (_, _) => { if (dragging is null) Hover(preview.Hovered, IndexOf(tool)); };
        pill.Trash.MouseLeave += (_, _) => { if (dragging is null) Hover(preview.Hovered, null); };
        pill.Trash.MouseLeftButtonUp += (_, _) =>
        {
            config.Wheel.RemoveAll(t => t.Same(tool));
            Save();
            LoadWheel(glide: true);
            SyncRows();
        };

        pill.Handle.MouseEnter += (_, _) =>
        {
            if (dragging is not null) return;
            Hover(IndexOf(tool), null);
            preview.PointAt(IndexOf(tool));
        };
        pill.Handle.MouseLeave += (_, _) => { if (dragging is null) Hover(null, null); };
        pill.Handle.MouseLeftButtonDown += (_, e) =>
        {
            dragging = KeyOf(tool);
            pill.Handle.CaptureMouse();
            Hover(IndexOf(tool), null);
            e.Handled = true;
        };
        pill.Handle.MouseMove += (_, e) =>
        {
            if (dragging != KeyOf(tool)) return;
            // Point anywhere outside the knob picks the slice in that direction.
            var p = e.GetPosition(pills);
            int from = IndexOf(tool);
            if (Look.Slice(p.X - EditorSize / 2, p.Y - EditorSize / 2, config.Wheel.Count, double.PositiveInfinity) is not int to || to == from) return;
            config.Wheel.RemoveAt(from);
            config.Wheel.Insert(to, tool);
            LoadWheel(glide: true);
            Hover(to, null);
            preview.PointAt(to);
        };
        pill.Handle.MouseLeftButtonUp += (_, _) =>
        {
            if (dragging is null) return;
            dragging = null;
            pill.Handle.ReleaseMouseCapture();
            Save();
            Hover(pill.Handle.IsMouseOver ? IndexOf(tool) : null, null);
        };
        return pill;
    }

    void Hover(int? hovered, int? trash)
    {
        preview.SetHover(hovered, trash);
        RestylePills();
    }

    void RestylePills()
    {
        foreach (var pill in pillsByTool.Values)
        {
            int i = IndexOf(pill.Tool);
            bool active = dragging == KeyOf(pill.Tool) || (dragging is null && preview.Hovered == i);
            bool trash = preview.TrashHovered == i;
            pill.Background = White(active || trash ? 0.09 : 0.04);
            pill.Trash.Foreground = trash ? Look.AccentBrush : White(0.45);
            foreach (Ellipse dot in pill.Dots.Children) dot.Fill = White(active ? 0.85 : 0.45);
        }
    }

    // ---- The app list ----

    /// One installed app. Click to put it on the wheel, or take it off; a check shows it's there.
    sealed class Row : Border
    {
        public readonly Tool Tool;
        public readonly Image Icon = new() { Width = 24, Height = 24 };
        readonly TextBlock mark = new() { FontFamily = Look.Glyphs, FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        bool onWheel;

        public Row(Tool tool, Action<Row> toggle)
        {
            Tool = tool;
            Padding = new Thickness(10, 5, 10, 5);
            CornerRadius = new CornerRadius(7);
            Background = Brushes.Transparent;
            RenderOptions.SetBitmapScalingMode(Icon, BitmapScalingMode.HighQuality);
            var name = new TextBlock { Text = tool.Name, FontFamily = Look.Text, FontSize = 13, Foreground = White(0.88), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            var grid = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new(), new() { Width = GridLength.Auto } } };
            grid.Children.Add(Icon);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);
            Child = grid;
            MouseEnter += (_, _) => Restyle();
            MouseLeave += (_, _) => Restyle();
            MouseLeftButtonUp += (_, _) => toggle(this);
        }

        public bool OnWheel { set { onWheel = value; Restyle(); } }

        void Restyle()
        {
            Background = White(IsMouseOver ? 0.06 : 0);
            mark.Text = onWheel ? "" : "";  // check, plus
            mark.Foreground = onWheel ? Look.AccentBrush : White(0.45);
            mark.Opacity = onWheel || IsMouseOver ? 1 : 0;
        }
    }

    async void LoadApps()
    {
        var apps = await Apps.InstalledAsync();
        list.Children.Remove(noMatches);
        foreach (var app in apps)
        {
            var row = new Row(app, Toggle);
            rows.Add(row);
            list.Children.Add(row);
        }
        noMatches.Text = "No matches";
        list.Children.Add(noMatches);
        SyncRows();
        Filter();
        // Icons a few at a time, so the list is usable straight away.
        foreach (var row in rows)
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (row.Tool.Expanded is { } path) row.Icon.Source = Native.Icon(path, 48);
            }, DispatcherPriority.Background);
    }

    void Toggle(Row row)
    {
        if (IndexOf(row.Tool) is int i and >= 0) config.Wheel.RemoveAt(i);
        else config.Wheel.Add(row.Tool);
        Save();
        LoadWheel(glide: true);
        SyncRows();
    }

    void SyncRows()
    {
        foreach (var row in rows) row.OnWheel = IndexOf(row.Tool) >= 0;
    }

    void Filter()
    {
        string query = search.Text.Trim();
        int shown = 0;
        foreach (var row in rows)
        {
            bool match = query == "" || row.Tool.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            row.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) shown++;
        }
        noMatches.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Bottom row ----

    FrameworkElement LoginSwitch()
    {
        var knob = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White, Margin = new Thickness(2) };
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), Child = knob, Cursor = Cursors.Hand };
        void Show()
        {
            bool on = LaunchAtLogin.IsEnabled;
            track.Background = on ? Look.AccentBrush : White(0.18);
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        track.MouseLeftButtonUp += (_, _) => { LaunchAtLogin.Set(!LaunchAtLogin.IsEnabled); Show(); };
        Show();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { track, new TextBlock { Text = "Open at login", FontFamily = Look.Text, FontSize = 12, Foreground = White(0.6), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } },
        };
    }

    /// Click / Release, like the Mac's segmented control.
    FrameworkElement OpenMode()
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        void Show()
        {
            foreach (Border segment in segments.Children)
            {
                bool on = (bool)segment.Tag == (config.ReleaseToOpen ?? false);
                segment.Background = White(on ? 0.16 : 0);
                ((TextBlock)segment.Child).Foreground = White(on ? 0.92 : 0.55);
            }
        }
        foreach (var (text, release) in new[] { ("Click", false), ("Release", true) })
        {
            var segment = new Border
            {
                Tag = release, CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 2, 10, 3), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, FontFamily = Look.Text, FontSize = 12 },
            };
            segment.MouseLeftButtonUp += (_, _) => { config.ReleaseToOpen = release; Save(); Show(); };
            segments.Children.Add(segment);
        }
        Show();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
            ToolTip = "Release: hover a tool and let go of the shortcut to open it",
            Children =
            {
                new TextBlock { Text = "Open apps by", FontFamily = Look.Text, FontSize = 12, Foreground = White(0.6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                new Border { Background = White(0.06), CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = segments },
            },
        };
    }

    // Shortcut recorder: click the keys, then press and release a new combination of two or more modifier keys.
    // Esc, a second click or leaving the window cancels. The arrow restores the default.
    DispatcherTimer? recorder;
    Mods held;
    Action? showShortcut;

    FrameworkElement ShortcutField()
    {
        var caption = new TextBlock { FontFamily = Look.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var restore = new TextBlock { Text = "", FontFamily = Look.Glyphs, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, ToolTip = $"Restore {Keys.Describe(Config.DefaultTrigger)}" };
        var keys = new TextBlock { FontFamily = Look.Text, FontSize = 13, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, MinWidth = 44 };
        var button = new Border { Child = keys, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6), Background = White(0.06), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Click, then hold the keys you want" };
        string? hint = null;

        showShortcut = () =>
        {
            bool isDefault = config.Trigger == Config.DefaultTrigger;
            caption.Text = hint ?? (Recording ? "Press keys" : "Shortcut");
            caption.Foreground = hint is null ? White(0.6) : Look.AccentBrush;
            restore.Foreground = White(isDefault ? 0.2 : 0.55);
            restore.Cursor = isDefault ? Cursors.Arrow : Cursors.Hand;
            keys.Text = Recording ? (held == Mods.None ? "…" : Keys.Describe(held)) : Keys.Describe(config.Trigger);
            keys.Foreground = Recording ? Look.AccentBrush : White(0.9);
            button.BorderBrush = Recording ? Look.AccentBrush : Brushes.Transparent;
        };
        restore.MouseLeftButtonUp += (_, _) =>
        {
            if (config.Shortcut is null) return;
            config.Shortcut = null;
            Save();
            showShortcut();
        };
        button.MouseLeftButtonUp += (_, _) =>
        {
            if (Recording) { StopRecording(); return; }
            Recording = true;
            held = Mods.None;
            hint = null;
            // Polls like the wheel does: WPF doesn't reliably see the Win key on its own.
            recorder = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
            {
                var now = Native.HeldMods();
                if (now != Mods.None)
                {
                    if ((now & (Mods.Win | Mods.Alt)) != 0 && (held & (Mods.Win | Mods.Alt)) == 0) Native.TapMaskKey();  // no Start menu on release
                    if ((now & held) == held) held = now;  // remember the most keys held at once
                }
                else if (Keys.Count(held) >= 2)
                {
                    config.Shortcut = (int)held;
                    Save();
                    StopRecording();
                    return;
                }
                else if (held != Mods.None)
                {
                    hint = "Use two or more keys";
                    held = Mods.None;
                }
                showShortcut();
            }, Dispatcher);
            showShortcut();
        };
        showShortcut();
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { caption, restore, button } };
    }

    void StopRecording()
    {
        recorder?.Stop();
        recorder = null;
        Recording = false;
        held = Mods.None;
        showShortcut?.Invoke();
    }
}
