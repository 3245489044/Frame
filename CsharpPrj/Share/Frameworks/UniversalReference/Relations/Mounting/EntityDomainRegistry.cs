using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 实体键文法（域自持；框架不拼串）。归一失败必须给出原因，禁静默丢键。
/// </summary>
public interface IEntityKeyGrammar
{
    /// <summary>归一：去空白 / 命名空间 / 实例身份；失败返回 false + reason。</summary>
    bool TryNormalize(string raw, string? ns, out string key, out string reason);

    /// <summary>端点键（图端点）与行键的映射；域自行拥有。</summary>
    string EndpointKey(string key, string? ns);
}

/// <summary>
/// 实体域 SPI（轴 A）：键文法 / 存在性探针 / 端点镜像。
/// <para>未装配探针或镜像 ⇒ 保留 null，由网关按姿态可见化（不伪装成事实）。</para>
/// </summary>
public interface IEntityDomain
{
    string EntityType { get; }

    IEntityKeyGrammar Keys { get; }

    IEntityExistenceProbe? Probe { get; }

    Func<string, CancellationToken, Task>? Mirror { get; }
}

/// <summary>实体域描述（数据化：新增实体 = 注册一行）。</summary>
public sealed record EntityDomain(
    string EntityType,
    IEntityKeyGrammar Keys,
    IEntityExistenceProbe? Probe = null,
    Func<string, CancellationToken, Task>? Mirror = null) : IEntityDomain;

/// <summary>
/// 实体域注册表（唯一注册点；网关不认识 history/task/node 字符串）。
/// </summary>
public sealed class EntityDomainRegistry
{
    private readonly Dictionary<string, IEntityDomain> _domains = new(StringComparer.Ordinal);

    /// <summary>注册域（重复 ⇒ 抛：禁静默覆盖）。</summary>
    public void Register(IEntityDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain.EntityType);
        if (!_domains.TryAdd(domain.EntityType, domain))
            throw new InvalidOperationException($"实体域重复注册：{domain.EntityType}（单一实现原则）");
    }

    /// <summary>取域（未注册 ⇒ null）。</summary>
    public IEntityDomain? TryGet(string entityType)
        => !string.IsNullOrWhiteSpace(entityType) && _domains.TryGetValue(entityType, out var found) ? found : null;

    /// <summary>已注册域数（结构计数，门禁判据）。</summary>
    public int Count => _domains.Count;
}

/// <summary>身份键文法（history / task / session：trim 后非空即可；保留实例身份）。</summary>
public sealed class IdentityKeyGrammar : IEntityKeyGrammar
{
    public static readonly IdentityKeyGrammar Instance = new();

    public bool TryNormalize(string raw, string? ns, out string key, out string reason)
    {
        key = (raw ?? string.Empty).Trim();
        reason = key.Length == 0 ? "empty-key" : string.Empty;
        return key.Length > 0;
    }

    public string EndpointKey(string key, string? ns) => key;
}

/// <summary>
/// 节点键文法：trim 后非空；**保留 <c>{node}@{instance}</c> 的实例身份**（设计单 §2.3），
/// 不剥 <c>@</c>/<c>:</c> 前缀（那会破坏端点身份）。
/// </summary>
public sealed class NodeKeyGrammar : IEntityKeyGrammar
{
    public static readonly NodeKeyGrammar Instance = new();

    public bool TryNormalize(string raw, string? ns, out string key, out string reason)
    {
        key = (raw ?? string.Empty).Trim();
        reason = key.Length == 0 ? "empty-node-key" : string.Empty;
        return key.Length > 0;
    }

    public string EndpointKey(string key, string? ns) => key;
}
