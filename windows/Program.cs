using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ToolWheel;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, "ToolWheel.SingleInstance", out bool first);
        if (!first) return;  // already running (it lives in the tray)

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

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Edit tools…", null, (_, _) => EditTools());
        var login = new System.Windows.Forms.ToolStripMenuItem("Open at login") { Checked = LaunchAtLogin.IsEnabled, CheckOnClick = true };
        login.CheckedChanged += (_, _) => LaunchAtLogin.Set(login.Checked);
        menu.Items.Add(login);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit Tool Wheel", null, (_, _) => Quit());
        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Tool Wheel",
            ContextMenuStrip = menu,
            Visible = true,
        };
        if (firstRun)
            tray.ShowBalloonTip(5000, "Tool Wheel is running", $"Hold {Describe(config.Trigger)} anywhere to open the wheel.", System.Windows.Forms.ToolTipIcon.None);

        // ponytail: polls key state at 60Hz, which needs no keyboard hook. Switch to a low-level hook if a
        // non-modifier shortcut (e.g. Ctrl+Space) is ever wanted.
        new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick(), Dispatcher.CurrentDispatcher).Start();
    }

    void Tick()
    {
        if (++ticks % 60 == 0) ReloadIfEdited();  // picks up a changed shortcut without a restart

        bool held = Native.HeldMods() == config.Trigger;
        if (!held) waitForRelease = false;

        if (held && !wheel.IsVisible && !waitForRelease) Open();
        else if (!held && wheel.IsVisible)
        {
            if (config.ReleaseToOpen == true)
            {
                if (wheel.HoveredTool is { } tool) tool.Launch();
                else if (wheel.GearHovered) EditTools();
            }
            wheel.Hide();
        }

        if (wheel.IsVisible)
        {
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(wheel.Handle, out var r);
            double dx = (p.X - (r.Left + r.Right) / 2.0) / scale, dy = (p.Y - (r.Top + r.Bottom) / 2.0) / scale;
            if (wheel.Track(dx, dy)) clicker.Click();
        }
    }

    void Open()
    {
        config = Store.Load();
        wheel.Load(config.Wheel);

        // Centre on the cursor, nudged inward if it would spill off the screen.
        Native.GetCursorPos(out var p);
        var (work, monitorScale) = Native.MonitorAt(p);
        scale = monitorScale;
        int size = (int)Math.Round(Look.Size * scale);
        int x = Math.Clamp(p.X - size / 2, work.Left, Math.Max(work.Left, work.Right - size));
        int y = Math.Clamp(p.Y - size / 2, work.Top, Math.Max(work.Top, work.Bottom - size));
        wheel.Show();
        Native.PlaceTopmost(wheel.Handle, x, y, size);
        wheel.PlayOpen();

        if ((config.Trigger & (Mods.Win | Mods.Alt)) != 0) Native.TapMaskKey();
    }

    void Click()
    {
        if (wheel.GearHovered)
        {
            wheel.Hide();
            waitForRelease = true;
            EditTools();
        }
        else if (wheel.HoveredTool is { } tool)
        {
            wheel.Press();
            tool.Launch();
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

    /// ponytail: until the Settings window is ported, editing means the JSON file in Notepad.
    static void EditTools() => Process.Start("notepad.exe", $"\"{Store.ConfigPath}\"");

    void Quit()
    {
        tray.Visible = false;
        Application.Current.Shutdown();
    }

    static string Describe(Mods m) => string.Join("+",
        new[] { (Mods.Ctrl, "Ctrl"), (Mods.Alt, "Alt"), (Mods.Shift, "Shift"), (Mods.Win, "Win") }
            .Where(k => m.HasFlag(k.Item1)).Select(k => k.Item2));
}

static class LaunchAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ToolWheel";

    public static bool IsEnabled => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is not null;

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
