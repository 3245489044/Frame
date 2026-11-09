#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SurfacePaint.Contracts;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 指针来源（<see cref="ISurfacePointerSource"/> 的唯一实现）：事件驱动，无定时轮询。
/// <para>坐标归一化到窗口客户区 DIP（96 基准）；窗口外/未挂载 ⇒ 显式不可用。</para>
/// <para>输入源：应用消息过滤器（<see cref="IMessageFilter"/>）而非 <c>Form.MouseMove</c>——
/// WinForms 鼠标消息不冒泡，窗体级事件在子控件/分栏面板上收不到；过滤器能覆盖同一 UI 线程上的所有子 HWND。</para>
/// </summary>
public sealed class WinFormsPointerSource : ISurfacePointerSource, IMessageFilter
{
    private const int WmMouseMove = 0x0200;
    private const int WmMouseLeave = 0x02A3;

    /// <summary>进程内单例（无状态注册，状态按窗口更新）。</summary>
    public static readonly WinFormsPointerSource Instance = new();

    private static readonly ConditionalWeakTable<Form, object> Attached = new();
    private static readonly object Marker = new();

    [ThreadStatic]
    private static bool _filterInstalledOnThread;

    private WeakReference<Form>? _form;
    private float _x;
    private float _y;
    private long _stamp;
    private volatile bool _inside;

    private WinFormsPointerSource()
    {
    }

    /// <summary>指针状态变化（UI 线程；订阅方自行做节拍合并）。</summary>
    public event Action? Changed;

    /// <summary>端口就绪态（已挂窗口 ⇒ Ready）。</summary>
    public PortStatus Status
        => Current() is null
            ? PortStatus.Missing(PortKind.Pointer, "pointer-source-missing")
            : PortStatus.Ready(PortKind.Pointer);

    /// <summary>读取最近一次采样（窗口外 ⇒ <see cref="SurfacePointerState.Unavailable"/> 语义的坐标 + Inside=false）。</summary>
    public SurfacePointerState Read()
    {
        if (Current() is null) return SurfacePointerState.Unavailable;
        return new SurfacePointerState(_x, _y, _inside, _stamp);
    }

    /// <summary>挂载窗口输入源（幂等；UI 线程调用）。</summary>
    /// <param name="form">目标窗口。</param>
    public static void Ensure(Form? form)
    {
        if (form is null || form.IsDisposed) return;
        if (!Attached.TryGetValue(form, out _))
        {
            Attached.Add(form, Marker);
            form.Disposed += (_, _) => Instance.Detach(form);
        }
        Instance.Attach(form);
    }

    private void Attach(Form form)
    {
        _form = new WeakReference<Form>(form);
        if (_filterInstalledOnThread) return;
        _filterInstalledOnThread = true;
        // 过滤器挂在当前 UI 线程的消息循环上：子控件（含 SplitterPanel）的 WM_MOUSEMOVE 也会经过这里。
        Application.AddMessageFilter(this);
    }

    private void Detach(Form form)
    {
        if (_form is not null && _form.TryGetTarget(out var tracked) && ReferenceEquals(tracked, form))
        {
            _inside = false;
            _stamp = Environment.TickCount64;
        }
    }

    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseMove && m.Msg != WmMouseLeave) return false;
        var form = Current();
        if (form is null) return false;

        // 用系统光标位置而非消息坐标：鼠标消息可能落在不同子 HWND 上（客户坐标口径不一致），
        // 且 SplitterPanel 等容器控件不会把 MouseMove 冒泡给窗体。
        System.Drawing.Point client;
        try
        {
            client = form.PointToClient(Control.MousePosition);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (m.Msg == WmMouseMove)
        {
            if (form.ClientRectangle.Contains(client)) Publish(form, client);
            else ClearInside();
            return false;
        }

        // WM_MOUSELEAVE 只在光标确实离开窗口客户区时清空，避免「移入子控件」被误判为离开。
        if (!form.ClientRectangle.Contains(client)) ClearInside();
        return false;
    }

    private void Publish(Form form, System.Drawing.Point client)
    {
        var dpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96;
        _x = client.X * 96f / dpi;
        _y = client.Y * 96f / dpi;
        _inside = true;
        _stamp = Environment.TickCount64;
        _form = new WeakReference<Form>(form);
        Changed?.Invoke();
    }

    private void ClearInside()
    {
        if (!_inside) return;
        _inside = false;
        _stamp = Environment.TickCount64;
        Changed?.Invoke();
    }

    private Form? Current()
        => _form is not null && _form.TryGetTarget(out var form) && !form.IsDisposed && form.IsHandleCreated
            ? form
            : null;
}
