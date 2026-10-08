namespace BasicMethod.Declarative.Ledger;

/// <summary>位序映射项：位序 + 权威成员名 + 镜像成员名。</summary>
public sealed record LedgerMirrorMember(int Index, string Authoritative, string Mirror);

/// <summary>
/// 镜像位序映射计划：由两模型（权威 / 镜像）推出**逐位映射表**，供**通用运行时映射器**驱动
/// —— 替代逐模型手写「按位构造」代码（D-c′ 静默错位的根治手段）。
/// <para>
/// 与 <see cref="LedgerMirrorGuard"/> 分工：Guard 给「是否对齐」的判词（可作守卫测试断言），
/// 本件给「怎么对齐」的映射表（可作运行时驱动的数据）。
/// </para>
/// <para>
/// 未对齐即 **fail-fast**（阻断，不 warn；违规明细随异常携带），例外须显式登记
/// （<paramref name="renames"/> / <paramref name="allowTrailingSuffix"/>）。
/// 零 Roslyn 依赖：本件不产出 C# 源码，只产出**运行时映射表**（故不破坏零 ProjectReference 硬约束）。
/// </para>
/// </summary>
public static class LedgerMirrorPlan
{
    /// <summary>
    /// 逐位映射表（权威成员序）；两模型未对齐即抛 <see cref="DeclarationViolationException"/>。
    /// </summary>
    public static IReadOnlyList<LedgerMirrorMember> Map(
        Type authoritative,
        Type mirror,
        IReadOnlyDictionary<string, string>? renames = null,
        bool allowTrailingSuffix = false)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(mirror);

        var violations = LedgerMirrorGuard.Compare(authoritative, mirror, renames, allowTrailingSuffix);
        if (violations.Count > 0)
            throw new DeclarationViolationException(
                $"镜像 {mirror.Name} 与权威 {authoritative.Name} 未对齐（{violations.Count} 处）", violations);

        var source = LedgerMirrorGuard.Members(authoritative);
        var target = LedgerMirrorGuard.Members(mirror);
        var map = new List<LedgerMirrorMember>(source.Count);
        for (var index = 0; index < source.Count; index++)
            map.Add(new LedgerMirrorMember(index, source[index].Name, target[index].Name));

        return map;
    }

    /// <summary>对齐判定（不抛，只给判词）。</summary>
    public static bool CanMap(
        Type authoritative,
        Type mirror,
        IReadOnlyDictionary<string, string>? renames = null,
        bool allowTrailingSuffix = false)
        => LedgerMirrorGuard.IsAligned(authoritative, mirror, renames, allowTrailingSuffix);
}
