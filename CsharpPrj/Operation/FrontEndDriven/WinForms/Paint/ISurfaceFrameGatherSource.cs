#nullable enable

using Modules.Gather;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 窗口帧视图声明端口（声明单源 = 聚拢声明面 <c>gather-views.ushar</c>）：
/// 渲染器只消费声明，不在自身内联视图。
/// <para>不可用 ⇒ <see cref="View"/> 为 null；消费方须显式回退并可见化（禁静默内联）。</para>
/// <para>落点 = 载体层（Agent.Ui 已引用本工程），避免为单一端口新增 Share 依赖。</para>
/// </summary>
public interface ISurfaceFrameGatherSource
{
    /// <summary>窗口帧视图声明（未注入/装载失败 ⇒ null）。</summary>
    GatherViewSpec? View { get; }
}
