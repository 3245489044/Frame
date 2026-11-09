#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SurfacePaint.Contracts;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 指针来源（<see cref="ISurfacePointerSource"/> 的唯一实现）：事件驱动，无定时轮询。
/// <para>坐标归一化到窗口客户区 DIP（96 基准）；窗口外/未挂载 ⇒ 显式不可用。</para>
/// </summary>
public sealed class WinFormsPointerSource : ISurfacePointerSource
{
    /// <summary>进程内单例（无状态注册，状态按窗口更新）。</summary>
    public static readonly WinFormsPointerSource Instance = new();

    private static readonly ConditionalWeakTable<Form, object> Attached = new();
    private static readonly object Marker = new();

    private WeakReference<Form>? _form;
    private float _x;
    private float _y;
    private long _stamp;
    private bool _inside;

    private WinFormsPointerSource()
    {
    }

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

    /// <summary>挂载窗口事件（幂等；UI 线程调用）。</summary>
    /// <param name="form">目标窗口。</param>
    public static void Ensure(Form? form)
    {
        if (form is null || form.IsDisposed) return;
        if (Attached.TryGetValue(form, out _)) return;
        Attached.Add(form, Marker);
        Instance.Attach(form);
    }

    private void Attach(Form form)
    {
        _form = new WeakReference<Form>(form);
        form.MouseMove += OnMove;
        form.MouseLeave += OnLeave;
        form.Disposed += (_, _) => Detach(form);
    }

    private void Detach(Form form)
    {
        form.MouseMove -= OnMove;
        form.MouseLeave -= OnLeave;
        if (_form is not null && _form.TryGetTarget(out var tracked) && ReferenceEquals(tracked, form))
        {
            _inside = false;
        }
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (sender is not Form form) return;
        var dpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96;
        _x = e.X * 96f / dpi;
        _y = e.Y * 96f / dpi;
        _inside = true;
        _stamp = Environment.TickCount64;
        _form = new WeakReference<Form>(form);
    }

    private void OnLeave(object? sender, EventArgs e)
    {
        _inside = false;
        _stamp = Environment.TickCount64;
    }

    private Form? Current()
        => _form is not null && _form.TryGetTarget(out var form) && !form.IsDisposed && form.IsHandleCreated
            ? form
            : null;
}
