#nullable enable

using Modules.Gather;

namespace Strategy.SurfaceHosting;

/// <summary>
/// 窗口帧视图声明端口（声明单源 = 聚拢声明面 <c>gather-views.ushar</c>）：渲染器只消费声明，不在自身内联视图。
/// <para>不可用 ⇒ <see cref="View"/> 为 null；消费方须显式回退并可见化（禁静默内联）。</para>
/// <para>2026-11-09 重构：从 <c>Strategy.Refresh.Surface</c> 迁入独立的 SurfaceHosting 工程，
/// 使通用刷新框架零 Surface 依赖；载体仍只实现本端口，不写任何策略。</para>
/// </summary>
public interface ISurfaceFrameGatherSource
{
    /// <summary>窗口帧视图声明（未注入/装载失败 ⇒ null）。</summary>
    GatherViewSpec? View { get; }
}
