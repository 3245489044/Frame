#nullable enable

using System;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using Strategy.Refresh.Ports;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>光变化源：光场签名变化即标脏 <c>light</c>。</summary>
internal sealed class SurfaceLightRefreshSource : IRefreshSource
{
    private bool _started;

    public string Name => "surface-light";

    public event Action<string?>? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        SurfaceLightRegistry.Changed += OnChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        SurfaceLightRegistry.Changed -= OnChanged;
    }

    private void OnChanged() => Changed?.Invoke("light");
}

/// <summary>布局变化源：窗口尺寸/位置变化即标脏 <c>layout</c>。</summary>
internal sealed class SurfaceLayoutRefreshSource : IRefreshSource
{
    private readonly Form _form;
    private bool _started;

    internal SurfaceLayoutRefreshSource(Form form)
    {
        _form = form;
    }

    public string Name => "surface-layout";

    public event Action<string?>? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _form.Resize += OnChanged;
        _form.Move += OnChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _form.Resize -= OnChanged;
        _form.Move -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke("layout");
}
