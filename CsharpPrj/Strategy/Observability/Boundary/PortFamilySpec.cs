namespace Strategy.Observability.Boundary;

/// <summary>
/// 端口族声明（E16）：族内端口键集合 + 该族的实现登记点文案（键 / 文案均为不透明字符串，机制件不解析）。
/// </summary>
public sealed record PortFamilySpec(string FamilyId, string ImplementationPoint, IReadOnlyList<string> PortKeys)
{
    /// <summary>族内端口键（Ordinal 升序）。</summary>
    public IReadOnlyList<string> SortedKeys
        => PortKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// 族标注（实现类上声明）：族名 = 不透明字符串；登记点文案缺省由目录模板派生
/// （见 <see cref="PortFamilyCatalog"/>），显式给出则覆盖。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PortFamilyAttribute(string familyId) : Attribute
{
    /// <summary>族名（不透明字符串）。</summary>
    public string FamilyId { get; } = string.IsNullOrWhiteSpace(familyId)
        ? throw new ArgumentException("族名不能为空", nameof(familyId))
        : familyId;

    /// <summary>本族实现登记点文案（null = 用目录模板 + 族名派生）。</summary>
    public string? ImplementationPoint { get; init; }
}
