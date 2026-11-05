using System;
using System.Collections.Generic;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 域词表注册表（声明驱动）：域集合唯一来源 = 声明文件（产品侧装载后注入），框架不内置域词（KS5）。
/// </summary>
public sealed class KeyDomainRegistry
{
    private readonly HashSet<string> _ids;

    public KeyDomainRegistry(IEnumerable<string>? ids)
    {
        _ids = new HashSet<string>(StringComparer.Ordinal);
        if (ids is null) return;
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id)) _ids.Add(id.Trim());
        }
    }

    /// <summary>空注册表（未装载声明时的显式形态；非「静默通过」）。</summary>
    public static KeyDomainRegistry Empty { get; } = new(null);

    public int Count => _ids.Count;

    public IReadOnlyCollection<string> Ids => _ids;

    public bool Contains(KeyDomainId domain) => domain.IsDeclared && _ids.Contains(domain.Id);

    /// <summary>解析域标识；未登记 ⇒ 抛（禁调用点自造域词）。</summary>
    public KeyDomainId Require(string id)
    {
        var domain = KeyDomainId.Of(id);
        if (!_ids.Contains(domain.Id))
            throw new InvalidOperationException($"域标识未在声明中登记: {domain.Id}");
        return domain;
    }
}
