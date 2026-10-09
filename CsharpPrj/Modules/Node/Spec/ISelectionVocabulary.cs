using System;
using System.Collections.Generic;

namespace Modules.Node.Spec;

/// <summary>
/// 读侧取用语义词表端口（U3）：<c>all</c> 选择名 + <c>marks</c> 序值 + <c>origins</c> 受控词集。
/// <para>
/// 红线 3：<c>Modules.Node</c> 零上层依赖、零 Usharp —— 端口只收**中性**词表值；装载（读
/// <c>[Domain] marks/origins</c> 声明面）归 Agent 侧。未装配 ⇒ <see cref="NullSelectionVocabulary"/>
/// （回退既有字面量，逐字零行为变更）。
/// </para>
/// </summary>
public interface ISelectionVocabulary
{
    /// <summary>「全选」选择名（缺省 = <c>all</c>）。</summary>
    string AllSelectionId { get; }

    /// <summary><c>marks</c> 词表：名 → 序值（序为 C# 侧排序/比较依据）。</summary>
    IReadOnlyDictionary<string, int> MarkOrder { get; }

    /// <summary><c>origins</c> 受控词集（<c>Live</c>/<c>Snapshot</c>/<c>Derived</c> 校验白名单）。</summary>
    IReadOnlyCollection<string> Origins { get; }
}

/// <summary>未装配词表：回退既有字面量（<c>all</c>）+ 空词表（零行为变更）。</summary>
public sealed class NullSelectionVocabulary : ISelectionVocabulary
{
    public static readonly NullSelectionVocabulary Instance = new();

    private static readonly IReadOnlyDictionary<string, int> EmptyMarks =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private NullSelectionVocabulary()
    {
    }

    public string AllSelectionId => ExpressionSelectionPredicate.AllSelectionId;

    public IReadOnlyDictionary<string, int> MarkOrder => EmptyMarks;

    public IReadOnlyCollection<string> Origins => Array.Empty<string>();
}
