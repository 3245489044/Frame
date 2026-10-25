namespace Modules.Wiring.Diagnostics;

/// <summary>
/// 一条违规（判据载体 = 结构事实：结构码 + 坐标 + 两侧集合）。
/// <para><see cref="Sides"/> 承载可复算证据（端口坐标 / 形态 / 供给者清单），禁承载动机或收益叙述。</para>
/// </summary>
public sealed record WiringViolation(
    string Code,
    string WireId,
    string Detail,
    IReadOnlyList<string>? Sides = null)
{
    public static WiringViolation Create(string code, string wireId, string detail, params string[] sides)
        => new(code, wireId, detail, sides is { Length: > 0 } ? sides : null);

    public string Describe()
        => Sides is { Count: > 0 }
            ? $"{Code}:{WireId}:{Detail} [{string.Join(" | ", Sides)}]"
            : $"{Code}:{WireId}:{Detail}";
}
