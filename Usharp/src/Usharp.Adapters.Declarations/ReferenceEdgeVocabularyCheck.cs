using System;
using System.Collections.Generic;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 边常量 ↔ 声明面**双向对账报告**（D）：
/// <para>
/// <see cref="UndeclaredConstants"/> = C# 边常量有、声明面无 ⇒ **漂移（红）**（禁止「代码里有、声明里没有」的边类型）；
/// <see cref="UnbackedDeclarations"/> = 声明面有、C# 常量无 ⇒ 待接线或非归属族的声明边（**提示**，非红）。
/// </para>
/// </summary>
/// <param name="UndeclaredConstants">未在声明面登记的常量边类型（升序）。</param>
/// <param name="UnbackedDeclarations">无对应常量的声明边类型（升序）。</param>
public sealed record ReferenceEdgeVocabularyReport(
    IReadOnlyList<string> UndeclaredConstants,
    IReadOnlyList<string> UnbackedDeclarations)
{
    /// <summary>一致性判据：无「常量有而声明无」的漂移。</summary>
    public bool IsConsistent => UndeclaredConstants.Count == 0;

    /// <summary>单行摘要（写日志/门禁用）。</summary>
    public string Summary()
        => $"undeclared={UndeclaredConstants.Count} unbacked={UnbackedDeclarations.Count}"
           + (UndeclaredConstants.Count > 0 ? $" [{string.Join(",", UndeclaredConstants)}]" : string.Empty);
}

/// <summary>
/// 边词表对账（D）：C# 边类型常量集 与 声明面边类型集 的一致性判据。
/// <para>
/// 常量权威 = <see cref="ReferenceTypes"/>（不在此重定义）；声明集 = <c>UsharpRefEdgeEmitter.Emit(compilation).edgeTypes</c>
/// （<c>hierarchy-refs.ushar</c> 的 <c>[Ref] edge=</c>）。本类只做**集合对账**，不改变任一侧。
/// </para>
/// </summary>
public static class ReferenceEdgeVocabularyCheck
{
    /// <summary>归属族边常量（4 类；本族对账基线。工作流静态引用边不在此族）。</summary>
    public static readonly IReadOnlyList<string> HierarchyEdgeConstants = new[]
    {
        ReferenceTypes.HistoryOwnsTask,
        ReferenceTypes.CrossDbLink,
        ReferenceTypes.SessionOwns,
        ReferenceTypes.TaskOwnsTask,
    };

    /// <summary>
    /// 对账：<paramref name="declaredEdgeTypes"/>（声明面产出）↔ 归属族常量集。
    /// 未声明常量 ⇒ 红；声明但无常量 ⇒ 提示。
    /// </summary>
    public static ReferenceEdgeVocabularyReport Check(IEnumerable<string> declaredEdgeTypes)
    {
        var declared = ToSet(declaredEdgeTypes);
        var constants = ToSet(HierarchyEdgeConstants);

        var undeclared = constants.Where(c => !declared.Contains(c));
        var unbacked = declared.Where(d => !constants.Contains(d));

        return new ReferenceEdgeVocabularyReport(Ordered(undeclared), Ordered(unbacked));
    }

    private static HashSet<string> ToSet(IEnumerable<string>? values)
        => new((values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)), StringComparer.Ordinal);

    private static IReadOnlyList<string> Ordered(IEnumerable<string> values)
        => values.OrderBy(v => v, StringComparer.Ordinal).ToList();
}
