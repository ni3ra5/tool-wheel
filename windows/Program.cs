using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ToolWheel;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--snapshot" or "--snapshot-settings")
        {
            Snapshot.Run(settings: args[0] == "--snapshot-settings", args[1]);
            return;
        }

        using var single = new Mutex(true, "ToolWheel.SingleInstance", out bool first);
        if (!first) return;  // already running (it lives in the tray)
        try { LaunchAtLogin.MigrateRunKey(); } catch { }  // never worth failing to start over

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new Controller();
        app.Run();
        GC.KeepAlive(controller);
    }
}

/// Watches the shortcut, opens the wheel at the cursor, and handles clicks and releases.
sealed class Controller
{
    readonly WheelWindow wheel = new();
    readonly Clicker clicker = new();
    readonly System.Windows.Forms.NotifyIcon tray;
    SettingsWindow? settings;
    Config config;
    DateTime configStamp;
    bool waitForRelease;  // after opening something, don't reopen until the keys are let go
    double scale = 1;     // the wheel's monitor scale; cursor maths is in physical pixels
    int ticks;

    public Controller()
    {
        bool firstRun = !File.Exists(Store.ConfigPath);
        config = Store.Load();
        configStamp = File.GetLastWriteTimeUtc(Store.ConfigPath);

        wheel.MouseDown += (_, _) => Click();
        // The click's audio stream is open only while the wheel is; closed a moment after, so the last click finishes.
        var silence = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        silence.Tick += (_, _) => { silence.Stop(); if (!wheel.IsVisible) clicker.Stop(); };
        wheel.IsVisibleChanged += (_, _) =>
        {
            if (wheel.IsVisible) clicker.Start();
            else silence.Start();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var open = menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);  // what double-click does
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit Tool Wheel", null, (_, _) => Quit());
        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Tool Wheel",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => OpenSettings();
        if (firstRun)
            tray.ShowBalloonTip(5000, "Tool Wheel is running", $"Hold {Keys.Describe(config.Trigger)} anywhere to open the wheel.", System.Windows.Forms.ToolTipIcon.None);

        // ponytail: polls key state at 60Hz, which needs no keyboard hook. Switch to a low-level hook if a
        // non-modifier shortcut (e.g. Ctrl+Space) is ever wanted.
        new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick(), Dispatcher.CurrentDispatcher).Start();
    }

    void Tick()
    {
        if (++ticks % 60 == 0) ReloadIfEdited();  // picks up a hand-edited shortcut without a restart
        if (SettingsWindow.Recording) return;     // holding a new shortcut in Settings mustn't open the wheel

        bool held = Native.HeldMods() == config.Trigger;
        if (!held) waitForRelease = false;

        if (held && !wheel.IsVisible && !waitForRelease) Open();
        else if (!held && wheel.IsVisible)
        {
            wheel.Hide();
            if (config.ReleaseToOpen == true)
            {
                if (wheel.View.HoveredTool is { } tool) Apps.Open(tool);
                else if (wheel.View.GearHovered) OpenSettings();
            }
        }

        if (wheel.IsVisible)
        {
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(wheel.Handle, out var r);
            double dx = (p.X - (r.Left + r.Right) / 2.0) / scale, dy = (p.Y - (r.Top + r.Bottom) / 2.0) / scale;
            if (wheel.View.Track(dx, dy)) clicker.Click();
        }
    }

    void Open()
    {
        config = Store.Load();
        wheel.View.Load(config.Wheel, Apps.Running(config.Wheel));

        // Centre on the cursor, nudged inward if it would spill off the screen.
        Native.GetCursorPos(out var p);
        var (work, monitorScale) = Native.MonitorAt(p);
        scale = monitorScale;
        int size = (int)Math.Round(Look.Size * scale);
        int x = Math.Clamp(p.X - size / 2, work.Left, Math.Max(work.Left, work.Right - size));
        int y = Math.Clamp(p.Y - size / 2, work.Top, Math.Max(work.Top, work.Bottom - size));
        wheel.Show();
        Native.PlaceTopmost(wheel.Handle, x, y, size);
        wheel.View.PlayOpen();

        if ((config.Trigger & (Mods.Win | Mods.Alt)) != 0) Native.TapMaskKey();
    }

    void Click()
    {
        if (wheel.View.GearHovered)
        {
            wheel.Hide();
            waitForRelease = true;
            OpenSettings();
        }
        else if (wheel.View.HoveredTool is { } tool)
        {
            wheel.View.Press();
            Apps.Open(tool);
            waitForRelease = true;
            wheel.Dispatcher.InvokeAsync(async () => { await Task.Delay(120); wheel.Hide(); });  // let the press show
        }
    }

    void ReloadIfEdited()
    {
        var stamp = File.Exists(Store.ConfigPath) ? File.GetLastWriteTimeUtc(Store.ConfigPath) : default;
        if (stamp == configStamp) return;
        configStamp = stamp;
        config = Store.Load();
    }

    void OpenSettings()
    {
        if (settings is null)
        {
            settings = new SettingsWindow(Store.Load(), saved =>
            {
                config = saved;
                configStamp = File.GetLastWriteTimeUtc(Store.ConfigPath);
            });
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        Native.Activate(settings.Handle);
    }

    void Quit()
    {
        tray.Visible = false;
        Application.Current.Shutdown();
    }
}

/// A shortcut in the user's Startup folder. Not the `HKCU\…\Run` key: on the test PC Windows skipped that entry at
/// sign-in and Task Manager never listed it, while a Startup shortcut to the same exe works and shows up there.
static class LaunchAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "ToolWheel";
    static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Tool Wheel.lnk");

    public static bool IsEnabled => File.Exists(Shortcut);

    public static void Set(bool on)
    {
        if (!on) { File.Delete(Shortcut); return; }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var link = shell.CreateShortcut(Shortcut);
        link.TargetPath = Environment.ProcessPath!;
        link.WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath!)!;
        link.Save();
    }

    /// Moves an "open at login" left in the Run key by earlier versions over to the shortcut.
    public static void MigrateRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(RunName) is null) return;
        key.DeleteValue(RunName);
        Set(true);
    }
}

/// `--snapshot out.png` draws the wheel (second tool hovered); `--snapshot-settings out.png` draws Settings.
/// For checking visuals without the shortcut, like the Mac's --snapshot. Reads tools.json but never writes it.
static class Snapshot
{
    public static void Run(bool settings, string path)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        FrameworkElement content;
        Window window;
        if (settings)
        {
            window = new SettingsWindow(Store.Load(), _ => { });
            content = (FrameworkElement)window.Content;
        }
        else
        {
            var view = new WheelView();
            var config = Store.Load();
            view.Load(config.Wheel, Apps.Running(config.Wheel));
            if (config.Wheel.Count > 1) view.SetHover(1, null);
            content = new System.Windows.Controls.Border { Child = view, Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x7B, 0x8C)) };  // a desktop-ish backdrop
            window = new Window { Content = content, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None };
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;  // off screen
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();

        // Long enough for the app list and its icons to load and the hover to settle.
        var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings ? 4 : 1) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            app.Dispatcher.InvokeAsync(() =>
            {
                var image = new RenderTargetBitmap((int)(content.ActualWidth * 2), (int)(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                image.Render(content);
                var png = new PngBitmapEncoder { Frames = { BitmapFrame.Create(image) } };
                using (var file = File.Create(path)) png.Save(file);
                app.Shutdown();
            }, DispatcherPriority.ContextIdle);
        };
        wait.Start();
        app.Run();
    }
}
