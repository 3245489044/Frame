#nullable enable

using System.Collections.Generic;
using Share.Conversation.Rendering.Surface;

namespace Share.SurfacePaint.Ambient;

/// <summary>
/// 窗口级快照光上下文（页面场/chrome 的唯一光源）：由窗口帧生产者提供，
/// 载体不再逐卡调用 <c>SurfaceLightPort.Sample</c>。
/// <para>无上下文（未接入 Refresh 的宿主/测试）⇒ 保留旧采样路径。</para>
/// </summary>
/// <param name="Light">本帧唯一光态（null ⇒ 无光）。</param>
/// <param name="Facts">本帧渲染事实（可空）。</param>
/// <param name="Generation">快照代次（缓存键；量化稳定）。</param>
public sealed record SurfaceFrameLightContext(
    SurfaceLightState? Light,
    SurfaceRenderFacts? Facts,
    long Generation);
