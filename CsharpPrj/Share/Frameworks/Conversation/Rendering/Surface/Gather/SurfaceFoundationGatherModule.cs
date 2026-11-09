#nullable enable

using System;
using System.Threading.Tasks;
using Modules.Gather;
using UiBackend;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 基础通用框架的取数源（宿主注入；缺省 = 显式不可用，不伪造能力）。
/// <para>宿主只声明「事实从哪来」，模块只做 location → 实现的闭合。</para>
/// </summary>
/// <param name="Composition">宿主合成能力。</param>
/// <param name="Backdrop">背后内容源。</param>
/// <param name="Budget">帧预算。</param>
/// <param name="Light">光帧（null = 引擎不可用）。</param>
/// <param name="Glass">玻璃选层（可空 ⇒ 由前四项经 <see cref="SurfaceGlassSpec.Clamp"/> 推导）。</param>
/// <param name="LightUnavailableReason">光不可用原因码。</param>
/// <param name="LightRead">光读取（三态：帧 / 显式不可用 / 失败）。</param>
public sealed record SurfaceFoundationSources(
    Func<SurfaceCompositionCapability> Composition,
    Func<SurfaceBackdropFrame> Backdrop,
    Func<SurfaceFrameBudget> Budget,
    Func<SurfaceLightFrame?> Light,
    Func<SurfaceGlassSpec>? Glass = null,
    string LightUnavailableReason = "engine-frame-unavailable",
    Func<SurfaceLightRead>? LightRead = null)
{
    /// <summary>缺省源：能力/背景显式不可用，光=None（引擎未接）。</summary>
    public static readonly SurfaceFoundationSources Unavailable = new(
        static () => SurfaceCompositionCapability.Unavailable,
        static () => SurfaceBackdropFrame.Unavailable,
        static () => SurfaceFrameBudget.Default,
        static () => null);
}

/// <summary>
/// 表面基础框架聚拢模块（**唯一 producer 点**）：surface-composition / surface-backdrop /
/// surface-glass / surface-light / surface-frame-budget。
/// <para>纪律：永不返回 null（null 会被聚拢视为失败）；不可用一律返回显式载荷 + 原因码。</para>
/// <para>本模块只产不用：消费由载体阶段（所有权表）决定。</para>
/// </summary>
public sealed class SurfaceFoundationGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly SurfaceFoundationSources _sources;

    /// <summary>构造模块。</summary>
    /// <param name="view">聚拢视图声明。</param>
    /// <param name="sources">宿主取数源（空 ⇒ 全部显式不可用）。</param>
    public SurfaceFoundationGatherModule(GatherViewSpec view, SurfaceFoundationSources? sources = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _sources = sources ?? SurfaceFoundationSources.Unavailable;
    }

    /// <inheritdoc />
    public string Name => "surface-foundation-gather";

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.FromView(_view);

        builder.ProvideFor(_view, "surface-composition",
            (_, _) => Task.FromResult<object?>(_sources.Composition()));
        builder.ProvideFor(_view, "surface-backdrop",
            (_, _) => Task.FromResult<object?>(_sources.Backdrop()));
        builder.ProvideFor(_view, "surface-glass",
            (_, _) => Task.FromResult<object?>(Glass()));
        builder.ProvideFor(_view, "surface-light",
            (_, _) => Task.FromResult<object?>(Light()));
        builder.ProvideFor(_view, "surface-frame-budget",
            (_, _) => Task.FromResult<object?>(_sources.Budget()));
    }

    private SurfaceGlassSpec Glass()
        => _sources.Glass?.Invoke()
           ?? SurfaceGlassSpec.Clamp(
               _sources.Composition(), _sources.Backdrop(), declared: null, _sources.Budget());

    private object? Light()
    {
        if (_sources.LightRead is { } readSource)
        {
            var read = readSource();
            if (read.Frame is { } frame) return frame;

            // E-22：取不到（失败）≠ 显式不可用；失败返回 null 让聚拢记逐域失败。
            if (read.Reason.StartsWith("light-source-failed", StringComparison.Ordinal)) return null;

            return new SurfaceLightUnavailable(
                string.IsNullOrWhiteSpace(read.Reason) ? "engine-frame-unavailable" : read.Reason);
        }

        return _sources.Light() is { } legacy
            ? legacy
            : new SurfaceLightUnavailable(
                string.IsNullOrWhiteSpace(_sources.LightUnavailableReason)
                    ? "engine-frame-unavailable"
                    : _sources.LightUnavailableReason);
    }
}
