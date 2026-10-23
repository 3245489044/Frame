using System;
using System.Collections.Generic;

namespace Modules.Declaration;

/// <summary>
/// 实现目录：已注册函数名集合 + 各函数的实现产出键集合（实现侧唯一声明来源）。
/// <para>产出键未登记的函数不参与静态端口闭合（灰度口径：先登记再收紧）。</para>
/// </summary>
public sealed class ImplementationCatalog
{
    private readonly HashSet<string> _registered;
    private readonly Dictionary<string, IReadOnlyList<string>> _produced;

    public ImplementationCatalog(
        IEnumerable<string>? registeredFunctions,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? producedPorts)
    {
        _registered = new HashSet<string>(StringComparer.Ordinal);
        if (registeredFunctions != null)
        {
            foreach (var name in registeredFunctions)
            {
                if (!string.IsNullOrWhiteSpace(name)) _registered.Add(name.Trim());
            }
        }

        _produced = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (producedPorts != null)
        {
            foreach (var pair in producedPorts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key)) _produced[pair.Key.Trim()] = pair.Value;
            }
        }
    }

    public static ImplementationCatalog Empty { get; } =
        new(Array.Empty<string>(), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public IReadOnlyCollection<string> RegisteredFunctions => _registered;

    public bool IsRegistered(string function)
        => !string.IsNullOrWhiteSpace(function) && _registered.Contains(function.Trim());

    public bool TryGetProduced(string function, out IReadOnlyList<string> keys)
    {
        if (!string.IsNullOrWhiteSpace(function) && _produced.TryGetValue(function.Trim(), out var found))
        {
            keys = found;
            return true;
        }

        keys = Array.Empty<string>();
        return false;
    }
}
