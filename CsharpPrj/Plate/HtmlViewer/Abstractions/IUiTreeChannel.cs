#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 键控对齐写端口（L1 通用契约）——把 <see cref="UiNode"/> 树按稳定键对齐到具名区域根（未变节点不动）。
/// 后端无关：HTML 由 <c>__uiReconcile.applyTree</c> 落地；写协调器经此单入口执行 Tree 计划。
/// </summary>
public interface IUiTreeChannel
{
    /// <summary>键控对齐具名区域根（rootName 为空 = 文档根），并对齐后回放交互态（滚动/焦点）。返回类型化写结果。</summary>
    Task<UiWriteResult> ReconcileAsync(string? rootName, UiNode tree, UiInteractionSnapshot? interaction = null, CancellationToken ct = default);
}
