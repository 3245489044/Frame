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
    private long _lastStableRevision;

    public string Name => "surface-light";

    public event Action<string?>? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _lastStableRevision = SurfaceLightRegistry.StableRevision;
        SurfaceLightRegistry.Changed += OnChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        SurfaceLightRegistry.Changed -= OnChanged;
    }

    /// <summary>仅「光真的变了」（世界/配置/覆盖区/网格）才标脏；epoch 秒桶不触发整窗重组。</summary>
    private void OnChanged()
    {
        var revision = SurfaceLightRegistry.StableRevision;
        if (revision == _lastStableRevision) return;
        _lastStableRevision = revision;
        Changed?.Invoke("light");
    }
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
