using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 键空间声明（数据，不是代码）：族 + 端口 + 策略 + 域词。构造期完成结构校验（重复族/空策略键/重复域词即抛）。
/// </summary>
public sealed record KeySpaceDeclaration(
    string Id,
    IReadOnlyList<KeyFamily> Families,
    IReadOnlyList<PortContract> Ports,
    IReadOnlyDictionary<string, EmptySetPolicy> Policies,
    IReadOnlyList<string>? Domains = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Derives = null,
    IReadOnlyList<string>? RequiredPolicies = null) : IKeySpaceDeclaration
{
    /// <summary>已声明回退链（名 → 字段序；首个非空即取；未声明 ⇒ 空表）。</summary>
    public IReadOnlyList<string> DeriveChain(string name)
        => Derives is not null && Derives.TryGetValue(name, out var chain)
            ? chain
            : Array.Empty<string>();

    /// <summary>已声明域词（声明驱动；空 = 未声明域段，框架不内置域词）。</summary>
    public IReadOnlyList<string> DomainIds => Domains ?? Array.Empty<string>();

    /// <summary>已声明必备策略键（声明面给出；空 = 未声明必备面）。</summary>
    public IReadOnlyList<string> RequiredPolicyIds => RequiredPolicies ?? Array.Empty<string>();

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

        foreach (var required in RequiredPolicyIds)
        {
            if (string.IsNullOrWhiteSpace(required))
            {
                throw new InvalidOperationException("必备策略键不能为空");
            }

            if (!Policies.ContainsKey(required))
            {
                throw new KeyNotFoundException(
                    $"键空间 '{Id}' 声明了必备策略 '{required}' 但未给出其值（requiredPolicies ⊆ policies 不成立）");
            }
        }

        var duplicateDomain = DomainIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(static id => id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateDomain is not null)
        {
            throw new InvalidOperationException($"域词重复声明: {duplicateDomain.Key}");
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

    /// <summary>尝试取空集策略（缺项 ⇒ false；调用方须显式可见化，禁静默取值）。</summary>
    public bool TryPolicy(string key, out EmptySetPolicy policy)
        => Policies.TryGetValue(key, out policy);

    /// <summary>取空集策略；未声明 ⇒ 抛（含键空间 id 与键名，禁调用点兜底）。</summary>
    public EmptySetPolicy PolicyOrThrow(string key)
        => Policies.TryGetValue(key, out var policy)
            ? policy
            : throw new KeyNotFoundException(
                $"空集策略未声明: '{key}'（键空间 '{Id}'；修复：在声明面 [KeySpace] 增 policy {key} | value=…）");

    public static KeySpaceDeclaration Empty(string id)
        => new(id, Array.Empty<KeyFamily>(), Array.Empty<PortContract>(),
            new Dictionary<string, EmptySetPolicy>(StringComparer.Ordinal),
            Array.Empty<string>());
}
