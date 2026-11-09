#nullable enable

using System;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using Strategy.Refresh.Ports;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>光变化源：环境光看 StableRevision，指针成员看 PointerRevision（分成员失效）。</summary>
internal sealed class SurfaceLightRefreshSource : IRefreshSource
{
    private bool _started;
    private long _lastStableRevision;
    private long _lastPointerRevision;

    public string Name => "surface-light";

    public event Action<string?>? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _lastStableRevision = SurfaceLightRegistry.StableRevision;
        _lastPointerRevision = SurfaceLightRegistry.PointerRevision;
        SurfaceLightRegistry.Changed += OnChanged;
        SurfaceLightRegistry.PointerChanged += OnPointerChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        SurfaceLightRegistry.Changed -= OnChanged;
        SurfaceLightRegistry.PointerChanged -= OnPointerChanged;
    }

    /// <summary>仅「光真的变了」（世界/配置/覆盖区/网格）才标脏；epoch 秒桶不触发整窗重组。</summary>
    private void OnChanged()
    {
        var revision = SurfaceLightRegistry.StableRevision;
        if (revision == _lastStableRevision) return;
        _lastStableRevision = revision;
        Changed?.Invoke("light");
    }

    /// <summary>指针成员独立失效：只标 <c>pointer</c>，不冒充整帧光变化。</summary>
    private void OnPointerChanged()
    {
        var revision = SurfaceLightRegistry.PointerRevision;
        if (revision == _lastPointerRevision) return;
        _lastPointerRevision = revision;
        Changed?.Invoke("pointer");
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
