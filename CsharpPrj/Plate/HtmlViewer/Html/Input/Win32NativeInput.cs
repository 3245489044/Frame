#nullable enable

using System;
using System.Runtime.InteropServices;

namespace UiBackend.Html;

/// <summary>
/// Win32 真实输入 P/Invoke（唯一原生声明点）——从控件方法体隔离，供 <see cref="RealInputClicker"/> 使用，
/// 使控件不再内联 user32 声明与原生结构。
/// </summary>
internal static class Win32NativeInput
{
    internal const uint INPUT_MOUSE = 0;

    // 虚拟屏度量（含多屏拼接原点偏移）：绝对坐标按虚拟桌面归一所需（E-8；替代旧主屏 SM_CXSCREEN/CYSCREEN）。
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    // 前台确权（真实输入前置）：Windows 前台锁会拒绝非前台进程的 SetForegroundWindow，
    // 通用做法 = 临时 AttachThreadInput 到当前前台线程后再 SetForegroundWindow/BringWindowToTop/SetFocus。
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    internal const int SW_RESTORE = 9;
    internal const uint GA_ROOT = 2;

    /// <summary>前台是否已是目标窗体（含其子窗口/被拥有的顶层窗口）。</summary>
    internal static bool IsForegroundOf(IntPtr formHandle)
    {
        if (formHandle == IntPtr.Zero) return false;
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (fg == formHandle) return true;
        if (IsChild(formHandle, fg)) return true;
        return GetAncestor(fg, GA_ROOT) == formHandle;
    }

    /// <summary>强制把目标窗体置前台（AttachThreadInput 绕过前台锁；失败不抛）。</summary>
    internal static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        var fg = GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0u : GetWindowThreadProcessId(fg, IntPtr.Zero);
        var curThread = GetCurrentThreadId();
        var attached = false;
        if (fgThread != 0 && fgThread != curThread)
            attached = AttachThreadInput(curThread, fgThread, true);
        try
        {
            ShowWindow(hWnd, SW_RESTORE);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            SetFocus(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(curThread, fgThread, false);
        }
    }

    // 命中测试（坐标回执取证）：返回屏幕点下的窗口句柄——用于判定真实点击坐标落在哪个窗口/控件。
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    /// <summary>控件所在窗口的 DPI（96=100%）。0=不可用；用于与 devicePixelRatio 交叉核对（不静默修正坐标）。</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    /// <summary>发送一次鼠标输入（move/down/up；绝对坐标按虚拟桌面归一）。返回系统是否接受该输入
    /// （SendInput 返回 0 = 被 UIPI/会话隔离拦截）。真实点击据此判 NOT_EFFECTIVE，不再无条件报 OK。</summary>
    internal static bool SendMouse(uint flags, int absX, int absY)
    {
        var input = new NativeInput { Type = INPUT_MOUSE };
        input.Mouse.dx = absX;
        input.Mouse.dy = absY;
        input.Mouse.dwFlags = flags;
        return SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>()) == 1;
    }
}
