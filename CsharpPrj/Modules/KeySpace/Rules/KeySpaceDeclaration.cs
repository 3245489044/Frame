using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键空间声明（数据，不是代码）：族 + 端口 + 策略。构造期完成结构校验（重复族/空策略键即抛）。
/// </summary>
public sealed record KeySpaceDeclaration(
    string Id,
    IReadOnlyList<KeyFamily> Families,
    IReadOnlyList<PortContract> Ports,
    IReadOnlyDictionary<string, EmptySetPolicy> Policies) : IKeySpaceDeclaration
{
    /// <summary>结构校验（幂等；返回自身便于链式构造）。</summary>
    public KeySpaceDeclaration Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("键空间声明缺少 id", nameof(Id));
        }

        var duplicateFamily = Families
            .GroupBy(static family => family.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateFamily is not null)
        {
            throw new InvalidOperationException($"键族重复声明: {duplicateFamily.Key}");
        }

        var duplicatePort = Ports
            .GroupBy(static port => (port.Name, port.Direction))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicatePort is not null)
        {
            throw new InvalidOperationException($"端口重复声明: {duplicatePort.Key.Name}");
        }

        foreach (var pair in Policies)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new InvalidOperationException("空集策略键不能为空");
            }
        }

        return this;
    }

    /// <summary>取空集策略；未声明 ⇒ 显式失败（禁调用点硬编码兜底）。</summary>
    public EmptySetPolicy Policy(string key)
        => Policies.TryGetValue(key, out var policy)
            ? policy
            : throw new KeyNotFoundException($"空集策略未声明: {key}");

    /// <summary>取空集策略（允许显式缺省；缺省值由调用方给出并可见）。</summary>
    public EmptySetPolicy PolicyOr(string key, EmptySetPolicy fallback)
        => Policies.TryGetValue(key, out var policy) ? policy : fallback;

    public static KeySpaceDeclaration Empty(string id)
        => new(id, Array.Empty<KeyFamily>(), Array.Empty<PortContract>(),
            new Dictionary<string, EmptySetPolicy>(StringComparer.Ordinal));
}
