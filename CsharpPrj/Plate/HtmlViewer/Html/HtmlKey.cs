#nullable enable

using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 受控键纲（KeySpace）单一源——把「参与对齐的稳定键」与「领域属性」**正交分离**（载体域分离，
/// 对齐 axioms:SEP.JudgmentCarrierStructural）：身份键/具名锚参与对齐，领域属性（data-session/history/…）
/// 仅为事件载荷，**不再参与对齐**（消除「同父两兄弟因共用领域属性被判同键」的语义过载）。
/// <para>C# 侧键守卫（<see cref="HtmlKeyGuard"/> / <see cref="HtmlKeyContract"/>）与注入 JS（<c>UiReconcileScript.__kk</c>）
/// 共用本处单一源，消除"增删键须改多处、漂移即静默丢态"。</para>
/// <para>纯结构常量（零领域语义、零状态）。</para>
/// </summary>
public static class HtmlKey
{
    /// <summary>身份键（IdentityKey）：节点对齐身份，**由渲染层派生**（<see cref="KeyPlanner"/>），作者不手写。</summary>
    public static readonly IReadOnlyList<string> IdentityKey = new[] { "data-key" };

    /// <summary>具名锚（NamedAnchor）：find-by-name（区域根 / 宿主写目标 / 脚本定位）；兼任身份（须过同父唯一）。</summary>
    public static readonly IReadOnlyList<string> NamedAnchor = new[] { "data-name", "id" };

    /// <summary>领域属性（DomainAttr）：事件载荷 / 上下文锚（点击桥读取）；**不参与对齐**。</summary>
    public static readonly IReadOnlyList<string> DomainAttr = new[]
    {
        "data-session", "data-history", "data-round", "data-pane", "data-cmd", "data-input",
    };

    /// <summary>参与对齐的稳定键（身份键 ∪ 具名锚），有序（优先序 = 身份键优先）。</summary>
    public static readonly IReadOnlyList<string> AlignAttributes =
        IdentityKey.Concat(NamedAnchor).ToArray();

    /// <summary>JS 字符串数组字面量（单一源：供内嵌脚本 <c>__kk</c> 注入；仅含参与对齐的属性，不含领域属性）。</summary>
    public static readonly string JsArrayLiteral =
        "[" + string.Join(",", AlignAttributes.Select(a => "'" + a + "'")) + "]";

    /// <summary>框架认可的对齐键属性（= <see cref="AlignAttributes"/>；有序，优先序即序列序）。</summary>
    public static readonly IReadOnlyList<string> Attributes = AlignAttributes;

    /// <summary>HTML 稳定键实例（L1 通用契约 <see cref="UiNodeKey"/> 的 HTML 落地；单一键源）。</summary>
    public static readonly UiNodeKey NodeKey = new(AlignAttributes);
}
