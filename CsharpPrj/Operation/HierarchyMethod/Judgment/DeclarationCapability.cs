using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>声明面能力（可空 / 条件产出 / 别名）：判据只读，不在规则内猜口径。</summary>
public sealed record DeclarationCapability(
    IReadOnlySet<string>? NullablePorts = null,
    IReadOnlySet<string>? ConditionalPorts = null,
    IReadOnlyDictionary<string, string[]>? Aliases = null)
{
    public static DeclarationCapability Empty { get; } = new();

    public bool IsNullable(string port) => NullablePorts?.Contains(port) == true;

    public bool IsConditional(string port) => ConditionalPorts?.Contains(port) == true;

    public IReadOnlyList<string> AliasesOf(string port)
        => Aliases is not null && Aliases.TryGetValue(port, out var aliases) ? aliases : Array.Empty<string>();

    public static IReadOnlySet<string> Set(IEnumerable<string>? values)
        => new HashSet<string>(
            values?.Where(static v => !string.IsNullOrWhiteSpace(v)) ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
}
