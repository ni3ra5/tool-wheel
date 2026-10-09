using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ToolWheel;

/// Asks GitHub Releases for a newer windows-v* release (at launch, or from "Check now") and, when the user says so,
/// installs it: downloads the zip, checks it against the release's SHA256SUMS, swaps in the new ToolWheel.exe and
/// restarts. Same flow as the Mac's Updater, which swaps the .app.
static class Updater
{
    const string Api = "https://api.github.com/repos/ni3ra5/tool-wheel/releases?per_page=30";
    public const string Page = "https://github.com/ni3ra5/tool-wheel/releases";

    public record Release(Version Version, string Notes, string ZipUrl, string? SumsUrl, string PageUrl);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2), DefaultRequestHeaders = { { "User-Agent", "ToolWheel" } } };

    /// This build's version (from the release tag); 0.0.0 for local builds, which never update.
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? new(v.Major, v.Minor, Math.Max(v.Build, 0)) : new(0, 0, 0);

    public static bool IsDevBuild => Current == new Version(0, 0, 0);

    /// The newest release for this PC if it's newer than this build, else null. Throws when GitHub can't be reached.
    public static async Task<Release?> CheckAsync()
    {
        if (IsDevBuild) return null;
        string zipEnd = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-win-arm64.zip" : "-win-x64.zip";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(Api));
        Release? newest = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) continue;
            if (!tag.StartsWith("windows-v") || !Version.TryParse(tag["windows-v".Length..], out var version)) continue;
            if (newest is not null && version <= newest.Version) continue;
            var assets = r.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToList();
            var zip = assets.FirstOrDefault(a => a.Name.EndsWith(zipEnd, StringComparison.OrdinalIgnoreCase)).Url;
            if (string.IsNullOrEmpty(zip)) continue;
            var sums = assets.FirstOrDefault(a => a.Name == "SHA256SUMS").Url;
            newest = new(version, CleanNotes(r.GetProperty("body").GetString()), zip, string.IsNullOrEmpty(sums) ? null : sums,
                r.GetProperty("html_url").GetString() ?? Page);
        }
        return newest is not null && newest.Version > Current ? newest : null;
    }

    /// GitHub's generated notes as plain lines: no headings, bold or "Full Changelog" link, bullets as â€¢.
    public static string CleanNotes(string? markdown) => string.Join("\n", (markdown ?? "").Replace("\r", "").Split('\n')
        .Where(l => !l.StartsWith("**Full Changelog**"))
        .Select(l => Regex.Replace(l.Trim().TrimStart('#').Trim(), @"^[*-] ", "â€¢ ").Replace("**", ""))
        .Where(l => l.Length > 0));

    /// Downloads, verifies and swaps in the new ToolWheel.exe, then starts it; the caller quits right after. A running
    /// .exe can be renamed but not overwritten, so this one steps aside as ToolWheel.exe.old (deleted at next launch).
    public static async Task InstallAsync(Release release, Action<string> status)
    {
        if (release.SumsUrl is null) throw new InvalidOperationException("this release has no checksums");
        status("Downloadingâ€¦");
        byte[] zip = await Http.GetByteArrayAsync(release.ZipUrl);
        string sums = await Http.GetStringAsync(release.SumsUrl);
        string name = Path.GetFileName(new Uri(release.ZipUrl).LocalPath);
        string expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].Trim().TrimStart('*') == name)
            .Select(p => p[0]).FirstOrDefault() ?? throw new InvalidOperationException($"no checksum for {name}");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(zip)), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("the download didn't match its checksum");

        status("Installingâ€¦");
        string exe = Environment.ProcessPath!, fresh = exe + ".new", old = exe + ".old";
        using (var archive = new ZipArchive(new MemoryStream(zip)))
            (archive.GetEntry("ToolWheel.exe") ?? throw new InvalidOperationException("the download has no ToolWheel.exe"))
                .ExtractToFile(fresh, overwrite: true);
        File.Delete(old);
        File.Move(exe, old);
        try { File.Move(fresh, exe); }
        catch { File.Move(old, exe); throw; }
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
    }

    /// Removes what the last update left behind.
    public static void CleanUp()
    {
        try { File.Delete(Environment.ProcessPath + ".old"); } catch { }  // still locked if the old copy hasn't quit; next time
    }

    public static void OpenPage(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// "Tool Wheel 0.1.5 is available", what's new, then Skip this version / Later / Update now. Same layout, wording and
/// sizes as the Mac's UpdateView: dark and flat like Settings, 420 wide.
sealed class UpdateWindow : Window
{
    static Brush White(double opacity) => Look.Frozen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 255, 255, 255)));
    static readonly Color Surface = Color.FromRgb(0x18, 0x18, 0x18);

    public UpdateWindow(Updater.Release release, Action skip, Action restart)
    {
        Title = "Tool Wheel Update";
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = new SolidColorBrush(Surface);

        TextBlock Label(string text, double size, double opacity, FontWeight? weight = null) => new()
        {
            Text = text, FontFamily = Look.Text, FontSize = size, Foreground = White(opacity), FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        Border Pill(string text, Brush background, Brush foreground) => new()
        {
            Background = background, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 5, 14, 6), Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
            Child = new TextBlock { Text = text, FontFamily = Look.Text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = foreground },
        };

        var notes = new ScrollViewer
        {
            MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 0),
            Content = new Border { Background = White(0.04), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = Label(release.Notes, 12, 0.75) },
            Visibility = release.Notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        var status = Label("", 12, 0.6);
        status.VerticalAlignment = VerticalAlignment.Center;
        var skipButton = Pill("Skip this version", Brushes.Transparent, White(0.55));
        skipButton.Margin = new Thickness(-14, 0, 0, 0);  // text flush with the title
        var later = Pill("Later", White(0.08), White(0.9));
        var update = Pill("Update now", Look.AccentBrush, Brushes.White);
        var page = Pill("Open download page", White(0.08), White(0.9));
        page.Visibility = Visibility.Collapsed;
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        DockPanel.SetDock(skipButton, Dock.Left);
        DockPanel.SetDock(status, Dock.Left);
        foreach (var b in new UIElement[] { update, later, page }) DockPanel.SetDock(b, Dock.Right);
        foreach (var b in new UIElement[] { skipButton, status, update, later, page }) buttons.Children.Add(b);

        skipButton.MouseLeftButtonUp += (_, _) => { skip(); Close(); };
        later.MouseLeftButtonUp += (_, _) => Close();
        page.MouseLeftButtonUp += (_, _) => { Updater.OpenPage(release.PageUrl); Close(); };
        update.MouseLeftButtonUp += async (_, _) =>
        {
            skipButton.Visibility = later.Visibility = update.Visibility = Visibility.Collapsed;
            try
            {
                await Updater.InstallAsync(release, s => status.Text = s);
                restart();
            }
            catch (Exception e)
            {
                status.Text = $"Couldn't update: {e.Message}";
                status.Foreground = Look.AccentBrush;
                status.MaxWidth = 220;
                later.Visibility = page.Visibility = Visibility.Visible;
                ((TextBlock)later.Child).Text = "Close";
            }
        };

        Content = new Border
        {
            Width = 420, Padding = new Thickness(24, 22, 24, 22),
            Child = new StackPanel
            {
                Children =
                {
                    Label($"Tool Wheel {release.Version} is available", 15, 0.92, FontWeights.SemiBold),
                    WithMargin(Label($"You have {Updater.Current}. Updating takes a few seconds and restarts Tool Wheel.", 12, 0.55), 6),
                    notes,
                    buttons,
                },
            },
        };
        SourceInitialized += (_, _) => Native.DarkTitleBar(new WindowInteropHelper(this).Handle, Surface, Color.FromRgb(0x99, 0x99, 0x99));
    }

    static T WithMargin<T>(T element, double top) where T : FrameworkElement { element.Margin = new Thickness(0, top, 0, 0); return element; }
}
