using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ToolWheel;

/// Opening tools the way the Mac does it: an app that already has a window comes to the front (restored if
/// minimised) instead of starting another copy; anything new is brought forward once its window appears.
static class Apps
{
    /// Start menu apps without an .exe of their own (Store apps, Settings) are saved as shell:AppsFolder\<app ID>.
    public const string Folder = @"shell:AppsFolder\";
    const StringComparison Ignore = StringComparison.OrdinalIgnoreCase;

    /// How to recognise a tool's windows: by its .exe, or by its app ID.
    static (string? Exe, string? AppId) Identity(Tool tool) => tool.Expanded switch
    {
        string p when p.StartsWith(Folder, Ignore) => (null, p[Folder.Length..]),
        string p when p.EndsWith(".exe", Ignore) => (p, null),
        _ => (null, null),
    };

    static bool Owns(Tool tool, Native.AppWindow window)
    {
        var (exe, id) = Identity(tool);
        if (id is not null) return string.Equals(id, window.AppId, Ignore);
        return exe is not null && window.Exe is not null && (string.Equals(exe, window.Exe, Ignore) || InSubfolder(exe, window.Exe));
    }

    /// Many apps start from a launcher and keep their windows in a program further down their own folder: Discord.exe
    /// runs app-1.0.9261\Discord.exe, Steam's window is bin\cef\…\steamwebhelper.exe, Opera's launcher runs
    /// <version>\opera.exe. A program right beside the tool's is another app (Word and Excel), and shared folders such
    /// as Windows or Program Files hold everyone's programs, so neither counts.
    static bool InSubfolder(string toolExe, string windowExe)
    {
        string folder = Path.GetDirectoryName(toolExe) + @"\", windowFolder = Path.GetDirectoryName(windowExe) + @"\";
        return windowFolder.Length > folder.Length && windowFolder.StartsWith(folder, Ignore) && !Shared(folder);
    }

    static readonly string[] SharedFolders = new[]
    {
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.UserProfile,
    }.Select(f => Environment.GetFolderPath(f) + @"\")
     .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") + @"\")
     .ToArray();

    static bool Shared(string folder) =>
        folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\", Ignore)
        || SharedFolders.Any(f => string.Equals(folder, f, Ignore));

    /// Per tool: whether it has a window open now (the wheel's grey dot).
    public static List<bool> Running(IEnumerable<Tool> tools)
    {
        var windows = Native.AppWindows();
        return tools.Select(t => windows.Any(w => Owns(t, w))).ToList();
    }

    public static void Open(Tool tool)
    {
        if (tool.Expanded is not { } target) return;
        var windows = Native.AppWindows();
        var existing = windows.FirstOrDefault(w => Owns(tool, w)).Handle;  // front-most first
        if (existing == IntPtr.Zero && Directory.Exists(target)) existing = FolderWindow(target);
        if (existing != IntPtr.Zero)
        {
            Native.Activate(existing);
            return;
        }

        Native.AllowNextToFront();
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"Couldn't open {target}: {e.Message}"); return; }
        BringForwardWhenOpen(tool, windows.Select(w => w.Handle).ToHashSet());
    }

    /// Windows can still open a new app behind the current one. Watch a few seconds for its window and raise it.
    static void BringForwardWhenOpen(Tool tool, HashSet<IntPtr> before)
    {
        if (Identity(tool) == (null, null)) return;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var window = Native.AppWindows().FirstOrDefault(w => !before.Contains(w.Handle) && Owns(tool, w)).Handle;
            if (window != IntPtr.Zero && Native.GetForegroundWindow() != window) Native.Activate(window);
            if (window != IntPtr.Zero || DateTime.UtcNow > deadline) timer.Stop();
        };
        timer.Start();
    }

    /// An Explorer window already showing this folder, if any.
    static IntPtr FolderWindow(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    string path = window.Document.Folder.Self.Path;
                    if (string.Equals(Path.TrimEndingDirectorySeparator(path), folder, Ignore)) return new IntPtr((long)window.HWND);
                }
                catch { }  // not a folder window
            }
        }
        catch (Exception e) { Debug.WriteLine($"Couldn't list Explorer windows: {e.Message}"); }
        return IntPtr.Zero;
    }

    /// The Start menu's All apps list, minus uninstallers, help files and web links, plus apps pinned to the taskbar
    /// or on the desktop. Desktop apps are saved by their .exe, everything else by app ID.
    public static Task<List<Tool>> InstalledAsync()
    {
        var done = new TaskCompletionSource<List<Tool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(Installed()); }
            catch (Exception e) { Debug.WriteLine($"Couldn't list apps: {e.Message}"); done.SetResult(new()); }
        });
        thread.SetApartmentState(ApartmentState.STA);  // the shell's COM objects want one
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    static List<Tool> Installed()
    {
        var apps = new Dictionary<string, Tool>(StringComparer.OrdinalIgnoreCase);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        foreach (dynamic item in shell.NameSpace("shell:AppsFolder").Items())
        {
            string name = item.Name, id = item.Path;
            string target = item.ExtendedProperty("System.Link.TargetParsingPath") as string ?? "";
            if (name.StartsWith("Uninstall", Ignore) || Path.GetFileName(target).StartsWith("unins", Ignore)) continue;
            // With arguments (Chrome web apps, Git Bash, mmc consoles) the .exe alone opens the wrong thing; open by ID
            // as Start does. Those may start another copy rather than reuse a window, as their windows rarely carry the ID.
            bool plain = string.IsNullOrWhiteSpace(item.ExtendedProperty("System.Link.Arguments") as string);
            string? path =
                id == "Microsoft.Windows.Explorer" ? @"%WINDIR%\explorer.exe"  // its windows carry no app ID
                : target.EndsWith(".exe", Ignore) && plain ? target
                : target == "" || target.StartsWith("::") || !plain ? Folder + id  // Store apps; shell places like Control Panel
                : null;                                                  // documents, help files, web links
            if (path is not null) apps.TryAdd(Environment.ExpandEnvironmentVariables(path), new Tool(name, path));
        }

        // Apps whose Start menu shortcut has gone (an installer skipped it, or it was deleted) but that are still
        // pinned to the taskbar or on the desktop.
        foreach (var folder in ShortcutFolders())
        {
            if (shell.NameSpace(folder) is not { } items) continue;
            foreach (dynamic item in items.Items())
            {
                string name = item.Name;
                string target = item.ExtendedProperty("System.Link.TargetParsingPath") as string ?? "";
                bool plain = string.IsNullOrWhiteSpace(item.ExtendedProperty("System.Link.Arguments") as string);
                if (target.EndsWith(".exe", Ignore) && plain && !Path.GetFileName(target).StartsWith("unins", Ignore))
                    apps.TryAdd(target, new Tool(name, target));
            }
        }
        return apps.Values.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static IEnumerable<string> ShortcutFolders() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    }.Where(Directory.Exists);
}
