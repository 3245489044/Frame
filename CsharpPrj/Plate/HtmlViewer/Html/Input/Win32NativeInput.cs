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

    // 命中测试（坐标回执取证）：返回屏幕点下的窗口句柄——用于判定真实点击坐标落在哪个窗口/控件。
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    /// <summary>发送一次鼠标输入（move/down/up；绝对坐标按虚拟桌面归一）。</summary>
    internal static void SendMouse(uint flags, int absX, int absY)
    {
        var input = new NativeInput { Type = INPUT_MOUSE };
        input.Mouse.dx = absX;
        input.Mouse.dy = absY;
        input.Mouse.dwFlags = flags;
        SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }
}
