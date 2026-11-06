#nullable enable

using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 后端投影契约（**载体侧唯一入口**）：同一 <see cref="RenderPlan"/> 投到不同载体。
/// <para>
/// 泛型产物：本层（平台层）不得引用任何载体类型（HTML 节点树 / WinForms 快照），
/// 故产物类型由实现决定；一致性判据（节点数/字段集合）由契约测试对拍。
/// </para>
/// </summary>
/// <typeparam name="TArtifact">载体产物类型。</typeparam>
public interface IBackendProjector<out TArtifact>
{
    /// <summary>后端 id（声明面 carrier 键：html / winforms / text …）。</summary>
    string BackendId { get; }

    /// <summary>投影（纯函数：同一计划必得同一产物）。</summary>
    /// <param name="plan">渲染计划。</param>
    TArtifact Project(RenderPlan plan);

    /// <summary>显式声明不支持的字段单元（可见降级；禁静默丢字段）。</summary>
    IReadOnlyList<UnsupportedSlot> UnsupportedCells { get; }
}
