using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolWheel;

/// The Win32 calls the app needs. Polling key state needs no hooks or special permissions, like the Mac version.
static class Native
{
    // ---- Keyboard ----

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static Mods HeldMods()
    {
        var m = Mods.None;
        if (Down(0x11)) m |= Mods.Ctrl;
        if (Down(0x12)) m |= Mods.Alt;
        if (Down(0x10)) m |= Mods.Shift;
        if (Down(0x5B) || Down(0x5C)) m |= Mods.Win;
        return m;
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    /// Taps an unassigned key (0xE8) while the shortcut is held, so releasing Win doesn't open Start
    /// and releasing Alt doesn't focus the app's menu bar (the trick AutoHotkey calls the "menu mask key").
    public static void TapMaskKey()
    {
        static INPUT Key(uint flags) => new() { type = 1, u = new() { ki = new() { wVk = 0xE8, dwFlags = flags } } };
        SendInput(2, new[] { Key(0), Key(2 /* KEYEVENTF_KEYUP */) }, Marshal.SizeOf<INPUT>());
    }

    // ---- Cursor, monitors, window placement (physical pixels) ----

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// Work area (excluding the taskbar) and scale factor of the monitor under a point.
    public static (RECT Work, double Scale) MonitorAt(POINT p)
    {
        var monitor = MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _);
        return (info.rcWork, dpi / 96.0);
    }

    public static void PlaceTopmost(IntPtr hwnd, int x, int y, int size) =>
        SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, x, y, size, size, 0x10 /* SWP_NOACTIVATE */ | 0x40 /* SWP_SHOWWINDOW */);

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// Clicks reach the wheel without stealing focus from the app underneath, and it stays out of Alt+Tab.
    public static void MakeNonActivating(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// Dark title bar in the window's own background colour, so it reads as one flat surface (colours need Windows 11).
    public static void DarkTitleBar(IntPtr hwnd, Color background, Color text)
    {
        static int Ref(Color c) => c.R | c.G << 8 | c.B << 16;  // COLORREF
        int on = 1, caption = Ref(background), title = Ref(text);
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4);
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 36 /* DWMWA_TEXT_COLOR */, ref title, 4);
    }

    // ---- Other apps' windows ----

    /// A window a person would Alt+Tab to, with the app it belongs to: its .exe, and its app ID if it has one
    /// (Store apps do; their windows often belong to ApplicationFrameHost.exe, so the .exe alone isn't enough).
    public readonly record struct AppWindow(IntPtr Handle, string? Exe, string? AppId);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder id);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr value, extra; }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);

    /// Open windows, front to back. Skips hidden, owned and tool windows, and cloaked ones (suspended Store apps,
    /// windows on other virtual desktops).
    public static List<AppWindow> AppWindows()
    {
        var found = new List<AppWindow>();
        var processes = new Dictionary<uint, (string? Exe, string? AppId)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            if ((GetWindowLong(hwnd, -20 /* GWL_EXSTYLE */) & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!processes.TryGetValue(pid, out var process)) processes[pid] = process = ProcessIdentity(pid);
            found.Add(new(hwnd, process.Exe, WindowAppId(hwnd) ?? process.AppId));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static (string?, string?) ProcessIdentity(uint pid)
    {
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero) return (null, null);
        try
        {
            var exe = new StringBuilder(1024);
            int exeLength = exe.Capacity, idLength = 256;
            var id = new StringBuilder(idLength);
            return (QueryFullProcessImageName(process, 0, exe, ref exeLength) ? exe.ToString() : null,
                    GetApplicationUserModelId(process, ref idLength, id) == 0 ? id.ToString() : null);
        }
        finally { CloseHandle(process); }
    }

    static string? WindowAppId(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };  // PKEY_AppUserModel_ID
            if (store.GetValue(ref key, out var value) != 0) return null;
            var id = value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.value) : null;
            PropVariantClear(ref value);
            return string.IsNullOrEmpty(id) ? null : id;
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint thread, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    /// Windows only lets the app in front hand focus on, and the wheel never takes focus. Sharing the front app's
    /// input queue for a moment lets us act as it.
    static void AsFrontApp(Action action)
    {
        uint front = GetWindowThreadProcessId(GetForegroundWindow(), out _), us = GetCurrentThreadId();
        bool joined = front != 0 && front != us && AttachThreadInput(us, front, true);
        try { action(); }
        finally { if (joined) AttachThreadInput(us, front, false); }
    }

    /// Brings a window to the front, restoring it if minimised (to maximised, if that's how it was).
    public static void Activate(IntPtr hwnd) => AsFrontApp(() =>
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    });

    /// Lets whatever starts next come to the front, rather than opening behind with a flashing taskbar button.
    public static void AllowNextToFront() => AsFrontApp(() => AllowSetForegroundWindow(-1 /* ASFW_ANY */));

    // ---- Icons: the shell's own icon for any path, shortcut, folder or Start menu app (shell:AppsFolder\…) ----

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint f0, f1, f2; public IntPtr dshSection; public uint dsOffset; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr obj, int size, out DIBSECTION dib);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    static IShellItemImageFactory? ShellItem(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        return SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) == 0 ? item : null;
    }

    /// True for a file, folder or Start menu app the shell can find.
    public static bool Exists(string path)
    {
        if (ShellItem(path) is not { } item) return false;
        Marshal.ReleaseComObject(item);
        return true;
    }

    static readonly Dictionary<(string, int), ImageSource?> icons = new();

    /// Icon at `pixels` square, cached for the session.
    public static ImageSource? Icon(string path, int pixels)
    {
        if (icons.TryGetValue((path, pixels), out var cached)) return cached;
        return icons[(path, pixels)] = LoadIcon(path, pixels);
    }

    static ImageSource? LoadIcon(string path, int pixels)
    {
        if (ShellItem(path) is not { } item) return null;
        try
        {
            if (item.GetImage(new SIZE { cx = pixels, cy = pixels }, 0x4 /* SIIGBF_ICONONLY */, out var bitmap) != 0) return null;
            try
            {
                GetObject(bitmap, Marshal.SizeOf<DIBSECTION>(), out var dib);
                BitmapSource image;
                if (dib.dsBm.bmBitsPixel == 32 && dib.dsBm.bmBits != IntPtr.Zero)
                {
                    // Copy the pixels ourselves: CreateBitmapSourceFromHBitmap drops the alpha channel.
                    var bm = dib.dsBm;
                    image = BitmapSource.Create(bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, bm.bmBits, bm.bmWidthBytes * bm.bmHeight, bm.bmWidthBytes);
                    if (dib.dsBmih.biHeight > 0) image = new TransformedBitmap(image, new ScaleTransform(1, -1));  // stored bottom-up
                }
                else image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { DeleteObject(bitmap); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }
}
