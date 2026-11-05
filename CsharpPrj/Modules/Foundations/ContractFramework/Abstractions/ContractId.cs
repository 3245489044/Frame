namespace ContractFramework.Abstractions;

/// <summary>
/// 契约标识（Name + Version，稳定、可反向引用）。
/// <para>声明形如 <c>"INV-1/KeyIdentitySeparation"</c>：<c>'/'</c> 前为不变量族号（可选），<c>'/'</c> 后为限定名；
/// 名字内可选 <c>'@'</c> 分隔版本（<c>KeyIdentitySeparation@1.0</c>）。</para>
/// <para><see cref="Segment"/> = 末段符号（限定名，不含版本），供门禁 G2 漂移比对（<c>\b</c> 匹配）。</para>
/// </summary>
public sealed class ContractId : IEquatable<ContractId>
{
    private ContractId(string raw, string? family, string name, string? version)
    {
        Raw = raw;
        Family = family;
        Name = name;
        Version = version;
    }

    /// <summary>原始声明串（如 <c>"INV-1/KeyIdentitySeparation"</c>）。</summary>
    public string Raw { get; }

    /// <summary>不变量族号（<c>'/'</c> 前缀；无则 <c>null</c>）。</summary>
    public string? Family { get; }

    /// <summary>限定名（末段，不含版本）。</summary>
    public string Name { get; }

    /// <summary>版本（可选）。</summary>
    public string? Version { get; }

    /// <summary>空标识（缺 <c>id</c> / 全空白）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Raw);

    /// <summary>末段符号（门禁 G2 载体）。</summary>
    public string Segment => Name;

    /// <summary>空标识单例（缺 <c>id</c> 时占位，供 <c>⊥</c> 可见化）。</summary>
    public static ContractId Empty { get; } = new(string.Empty, null, string.Empty, null);

    /// <summary>宽容解析（永不抛；空/非法 → 带原串的标识，<see cref="IsEmpty"/> 判空）。</summary>
    public static ContractId Parse(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Empty;
        }

        var slash = text.LastIndexOf('/');
        var family = slash >= 0 ? text[..slash] : null;
        var tail = slash >= 0 ? text[(slash + 1)..] : text;
        if (slash >= 0 && (family is null || family.Length == 0))
        {
            family = null;
        }

        string name = tail;
        string? version = null;
        var at = tail.LastIndexOf('@');
        if (at > 0 && at < tail.Length - 1)
        {
            name = tail[..at];
            version = tail[(at + 1)..];
        }

        return new ContractId(text, family, name, version);
    }

    public bool Equals(ContractId? other)
        => other is not null && string.Equals(Raw, other.Raw, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ContractId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Raw);

    public override string ToString() => Raw;
}
