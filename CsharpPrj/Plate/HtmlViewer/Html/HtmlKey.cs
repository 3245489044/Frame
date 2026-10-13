#nullable enable

using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 稳定键单一源（I2 键确定性 / D7 键集三处重复收敛）——「可保持状态节点」的稳定键属性集**仅此一处定义**：
/// C# 侧键守卫（<see cref="HtmlKeyGuard"/>）与注入 JS（<c>UiReconcileScript.__kk</c>）
/// 共用同一源，消除"增删键须改三处、漂移即静默丢态"。
/// <para>纯结构常量（零领域语义、零状态），对象域 = 稳定键属性名。优先序 = 字面量中的出现序（靠前者优先命中）。</para>
/// </summary>
public static class HtmlKey
{
    /// <summary>JS 字符串数组字面量（单一源：既供内嵌脚本 <c>__kk</c> 注入，亦为 <see cref="Attributes"/> 的解析源）。</summary>
    public const string JsArrayLiteral = "['data-key','data-name','data-history','data-session','data-round','data-pane','id']";

    /// <summary>框架认可的稳定键属性（由 <see cref="JsArrayLiteral"/> 派生；有序，优先序即字面量序）。</summary>
    public static readonly IReadOnlyList<string> Attributes =
        JsArrayLiteral.Trim('[', ']')
            .Split(',')
            .Select(s => s.Trim().Trim('\''))
            .Where(s => s.Length > 0)
            .ToArray();

    /// <summary>HTML 稳定键实例（L1 通用契约 <see cref="UiNodeKey"/> 的 HTML 落地；单一键源）。</summary>
    public static readonly UiNodeKey NodeKey = new(Attributes);
}
