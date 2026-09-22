using BasicMethod.Declarative.Spec;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 值类 → <see cref="ParameterKind"/> 的**单一映射表**：键集由 <see cref="UsharpLanguage.ValueKinds"/> 派生
/// （语言面是值类集合的唯一权威），本表只负责「值类 → 参数类」的对应。
/// <para>新增值类 = 在 <c>UsharpLanguage</c> 加常量 + 本表加一个 <c>case</c>；漏登记即在类型初始化期失败（不静默）。</para>
/// </summary>
public static class UsharpValueKindMap
{
    /// <summary>值类 → 参数类（序 = <see cref="UsharpLanguage.ValueKinds"/>）。</summary>
    public static readonly IReadOnlyList<(string ValueKind, ParameterKind Kind)> Table =
        UsharpLanguage.ValueKinds.Select(v => (v, Map(v))).ToList();

    /// <summary>值类是否合法（大小写不敏感；null/未知 → false）。</summary>
    public static bool IsKnown(string? valueKind) => TryParse(valueKind, out _);

    /// <summary>值类 → <see cref="ParameterKind"/>（null/未知 → false，由调用方决定诊断与兜底）。</summary>
    public static bool TryParse(string? valueKind, out ParameterKind kind)
    {
        foreach (var (value, mapped) in Table)
            if (string.Equals(value, valueKind, StringComparison.OrdinalIgnoreCase))
            {
                kind = mapped;
                return true;
            }

        kind = default;
        return false;
    }

    private static ParameterKind Map(string valueKind) => valueKind switch
    {
        UsharpLanguage.KindFlag => ParameterKind.Flag,
        UsharpLanguage.KindOrdinal => ParameterKind.Ordinal,
        UsharpLanguage.KindNumeric => ParameterKind.Numeric,
        _ => throw new InvalidOperationException(
            $"值类 '{valueKind}' 未在 UsharpValueKindMap 登记（合法值类权威 = UsharpLanguage.ValueKinds）"),
    };
}
