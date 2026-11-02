using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
using Modules.Node.Read;

namespace Share.NodeIo;

/// <summary>
/// 多域路由（**唯一域分发点**）：域键 → 已装配 <see cref="NodeDocumentFramework"/>；
/// 整键反查只扫“具体域”，显式通用域（fallback）不参与反查（消同形键歧义，禁静默择一）。
/// <b>无静态状态</b>，由组合根装配一次。
/// </summary>
public sealed class NodeDocumentRegistry
{
    private readonly Dictionary<string, NodeDocumentFramework> _frameworks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeDocumentFramework> _fallbacks = new(StringComparer.Ordinal);
    /// <summary>装配期缓存：该域是否参与全局整键反查（禁热路径每次重算键模板）。</summary>
    private readonly Dictionary<string, bool> _globalKeyScan = new(StringComparer.Ordinal);

    /// <summary>参与整键反查的具体域（不含显式通用域）。</summary>
    public IReadOnlyCollection<string> Domains => _frameworks.Keys.ToArray();

    /// <summary>显式通用域（不参与整键反查；只能由调用方显式给域名）。</summary>
    public IReadOnlyCollection<string> FallbackDomains => _fallbacks.Keys.ToArray();

    /// <summary>全部已注册域（= 具体域 ∪ 通用域；消除「Domains 含不含 fallback」的枚举歧义）。</summary>
    public IReadOnlyCollection<string> AllDomains =>
        _frameworks.Keys.Concat(_fallbacks.Keys).ToArray();

    public void Register(string domain, NodeDocumentFramework framework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(framework);
        if (!_frameworks.TryAdd(domain, framework))
        {
            throw new InvalidOperationException($"域重复注册: {domain}");
        }

        _globalKeyScan[domain] = SupportsGlobalKeyScan(framework);
    }

    /// <summary>
    /// 注册显式通用域：不参与 <see cref="TryResolveByKey"/> 扫描，只允许调用方显式给出域名。
    /// </summary>
    public void RegisterFallback(string domain, NodeDocumentFramework framework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(framework);
        if (!_fallbacks.TryAdd(domain, framework))
        {
            throw new InvalidOperationException($"通用域重复注册: {domain}");
        }
    }

    public bool TryGet(string domain, out NodeDocumentFramework framework) =>
        _frameworks.TryGetValue(domain, out framework!) || _fallbacks.TryGetValue(domain, out framework!);

    /// <summary>
    /// 整键 → 作用域：命中恰一个具体域 ⇒ true；0 ⇒ <c>key-unresolved</c>；≥2 ⇒ <c>key-ambiguous</c>（禁择一）。
    /// </summary>
    public bool TryResolveByKey(string key, out NodeDocumentScope scope, out string reason)
    {
        scope = new NodeDocumentScope(string.Empty, string.Empty, key);
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            reason = NodeDocumentReasons.KeyContextMissing;
            return false;
        }

        var hits = new List<(string Domain, NodeDocumentScope Scope)>();
        var failures = new List<string>();
        foreach (var pair in _frameworks)
        {
            // 全局反查只扫“有字面前缀”的域；键模板以占位符开头的域（如 task-node {storage}.{node}）
            // 是 catch-all 形态，只能由调用方显式给出域名（否则任何两段键都会被它吞掉 ⇒ 伪歧义）。
            if (!_globalKeyScan.GetValueOrDefault(pair.Key))
            {
                continue;
            }

            // 逐域隔离：单域坏声明/解析异常不得中断整键反查（失败原因计入 failures，最后统一回报）。
            try
            {
                if (pair.Value.Port.TryResolveByKey(pair.Key, key, out var hit, out var hitReason))
                {
                    hits.Add((pair.Key, hit));
                }
                else if (!string.IsNullOrWhiteSpace(hitReason))
                {
                    failures.Add($"{pair.Key}:{hitReason}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{pair.Key}:resolve-failed:{ex.Message}");
            }
        }

        if (hits.Count == 1)
        {
            scope = hits[0].Scope;
            return true;
        }

        if (hits.Count > 1)
        {
            reason = NodeDocumentReasons.KeyAmbiguous + ":" + string.Join(",", hits.Select(h => h.Domain));
            return false;
        }

        reason = NodeDocumentReasons.KeyUnresolved + ":" + key +
                 (failures.Count == 0
                     ? string.Empty
                     : "(" + string.Join(";", failures.Take(3)) +
                       (failures.Count > 3 ? ";+" + (failures.Count - 3) : string.Empty) + ")");
        return false;
    }

    public Task<NodeReadResult> QueryAsync(NodeDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!TryGet(query.Scope.Domain, out var framework))
        {
            return Task.FromResult(NodeDocumentResultFactory.Unavailable(
                query.Scope.Domain,
                new[] { NodeDocumentReasons.DomainUnregistered + ":" + query.Scope.Domain },
                new[] { NodeReadReason.DomainUnregistered }));
        }

        return framework.Port.QueryAsync(query, cancellationToken);
    }

    public Task<NodeMaterializationResult> ApplyAsync(
        NodeDocumentChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!TryGet(change.Scope.Domain, out var framework))
        {
            return Task.FromResult(Rejected(
                change.Scope,
                new[] { NodeDocumentReasons.DomainUnregistered + ":" + change.Scope.Domain }));
        }

        return framework.Port.ApplyAsync(change, cancellationToken);
    }

    public INodeStreamSession BeginStream(NodeDocumentScope scope, int round)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!TryGet(scope.Domain, out var framework))
        {
            throw new NodeDocumentStreamException(NodeDocumentReasons.DomainUnregistered + ":" + scope.Domain);
        }

        return framework.Port.BeginStream(scope, round);
    }

    private static bool SupportsGlobalKeyScan(NodeDocumentFramework framework) =>
        framework.Framework.Placements.All.Any(placement =>
            placement.IsReadable &&
            !placement.KeyTemplate.TrimStart().StartsWith("{", StringComparison.Ordinal));

    private static NodeMaterializationResult Rejected(NodeDocumentScope scope, IReadOnlyList<string> reasons) =>
        // 结果构造单源（D3）。
        NodeDocumentResultFactory.Rejected(scope, reasons);
}
