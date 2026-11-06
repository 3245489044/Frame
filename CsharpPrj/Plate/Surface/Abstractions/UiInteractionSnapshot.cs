#nullable enable

using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>滚动位（L1 通用契约）：具名节点的滚动位置（<paramref name="Bottom"/>=true 表示贴底）。</summary>
public sealed record UiScrollState(string Key, double Top, double Left, bool Bottom = false);

/// <summary>
/// 交互态快照（L1 通用契约，L5 态外置）——页面回流的**滚动/焦点**（展开集合由渲染层绑定，见键控树的 open）：
/// 键控对齐后由写原语按稳定键回放，使「态即数据」覆盖滚动/焦点（不再依赖 save/restore 启发式）。
/// </summary>
public sealed record UiInteractionSnapshot(
    string? Focus = null,
    IReadOnlyList<UiScrollState>? Scroll = null,
    IReadOnlyDictionary<string, string?>? Inputs = null)
{
    /// <summary>空快照。</summary>
    public static UiInteractionSnapshot Empty { get; } = new();

    /// <summary>是否无任何可回放态。</summary>
    public bool IsEmpty => Focus is null
        && (Scroll is null || Scroll.Count == 0)
        && (Inputs is null || Inputs.Count == 0);
}
