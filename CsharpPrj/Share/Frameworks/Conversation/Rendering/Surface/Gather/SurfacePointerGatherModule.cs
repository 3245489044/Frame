#nullable enable

using System;
using System.Threading.Tasks;
using Modules.Gather;
using SurfacePaint.Contracts;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 指针输入聚拢模块（**唯一 producer**）：未就绪 ⇒ 显式 <see cref="SurfacePointerState.Unavailable"/>，不回 null。
/// </summary>
public sealed class SurfacePointerGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly ISurfacePointerSource _source;

    /// <summary>构造模块。</summary>
    /// <param name="view">聚拢视图声明。</param>
    /// <param name="source">指针来源（载体实现）。</param>
    public SurfacePointerGatherModule(GatherViewSpec view, ISurfacePointerSource source)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public string Name => "surface-pointer-gather";

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.FromView(_view);
        builder.ProvideFor(_view, "surface-pointer", (_, _) => Task.FromResult<object?>(Read()));
    }

    private SurfacePointerState Read()
        => _source.Status.IsReady ? _source.Read() : SurfacePointerState.Unavailable;
}
