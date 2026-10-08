using System;
using System.Runtime.InteropServices;
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

    // ---- Icons: the shell's own 256px ("jumbo") icon for any path, shortcut or folder ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        // Methods before GetIcon are declared only to keep the vtable order right.
        [PreserveSig] int Add(IntPtr image, IntPtr mask, ref int index);
        [PreserveSig] int ReplaceIcon(int i, IntPtr icon, ref int index);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int i, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int mask, ref int index);
        [PreserveSig] int Draw(IntPtr parameters);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll")] static extern int SHGetImageList(int list, ref Guid iid, out IImageList images);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

    public static ImageSource? Icon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x4000 /* SHGFI_SYSICONINDEX */) == IntPtr.Zero)
            return null;
        var iid = typeof(IImageList).GUID;
        if (SHGetImageList(4 /* SHIL_JUMBO */, ref iid, out var images) != 0) return null;
        var handle = IntPtr.Zero;
        images.GetIcon(info.iIcon, 1 /* ILD_TRANSPARENT */, ref handle);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally { DestroyIcon(handle); }
    }
}
