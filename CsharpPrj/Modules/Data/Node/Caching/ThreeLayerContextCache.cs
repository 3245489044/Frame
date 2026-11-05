using System.Collections.Concurrent;

namespace Modules.Node.Caching;

/// <summary>
/// 上下文**三层派生缓存**端口（只读派生 Origin=Derived）：节点级 / 组装级 / prompt 前缀级。
/// <para>
/// 红线：缓存**只读派生**，绝不成为第二真源；未命中返回 <c>false</c>（由调用方重算并回填），
/// 不伪造空值 / 不兜底默认。键一律**内容指纹**驱动（禁 mtime）。
/// </para>
/// </summary>
public interface IDerivedContextCache
{
    /// <summary>节点级读：按 <paramref name="identityKey"/> + <paramref name="payloadHash"/> 取投影（未命中 ⇒ <c>false</c>）。</summary>
    bool TryGetNode(string identityKey, string payloadHash, out string? projected);

    /// <summary>节点级回填（同键新 hash ⇒ 覆盖为最新载荷投影）。</summary>
    void SetNode(string identityKey, string payloadHash, string projected);

    /// <summary>组装级读：按 <paramref name="assemblyKey"/> 取标准上下文（未命中 ⇒ <c>false</c>）。</summary>
    bool TryGetAssembly(string assemblyKey, out string? standardContext);

    /// <summary>组装级回填。</summary>
    void SetAssembly(string assemblyKey, string standardContext);

    /// <summary>prompt 前缀级读：按 <paramref name="prefixKey"/> 取前缀文本 + 序号锚点（未命中 ⇒ <c>false</c>）。</summary>
    bool TryGetPrefix(string prefixKey, out string? prefix, out int anchor);

    /// <summary>prompt 前缀级回填。</summary>
    void SetPrefix(string prefixKey, string prefix, int anchor);

    /// <summary>当前缓存项数（可见化 / 埋点用；只读，不参与判据）。</summary>
    int Count { get; }
}

/// <summary>
/// 上下文**三层缓存**实现：进程内三表（节点级 / 组装级 / prompt 前缀级）。
/// <para>
/// 纯**只读派生**面（Origin=Derived），绝不成为第二真源：未命中返回 <c>false</c>，由调用方重算回填；
/// 不伪造空值、不兜底默认。键一律**内容指纹**驱动（禁 mtime）。线程安全（并发读写由
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> 承担）。
/// </para>
/// <para>挂接方式（默认关，零回归）：调用方按开关决定是否经本缓存；关闭时与现状逐字一致。</para>
/// </summary>
public sealed class ThreeLayerContextCache : IDerivedContextCache
{
    private readonly ConcurrentDictionary<string, string> _nodes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _assemblies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Prefix, int Anchor)> _prefixes = new(StringComparer.Ordinal);

    /// <summary>节点级键 = <c>identityKey + "\u0001" + payloadHash</c>（载荷变 ⇒ hash 变 ⇒ 键变）。</summary>
    private static string NodeKey(string identityKey, string payloadHash) => identityKey + "\u0001" + payloadHash;

    public bool TryGetNode(string identityKey, string payloadHash, out string? projected)
        => _nodes.TryGetValue(NodeKey(identityKey, payloadHash), out projected);

    public void SetNode(string identityKey, string payloadHash, string projected)
        => _nodes[NodeKey(identityKey, payloadHash)] = projected;

    public bool TryGetAssembly(string assemblyKey, out string? standardContext)
        => _assemblies.TryGetValue(assemblyKey, out standardContext);

    public void SetAssembly(string assemblyKey, string standardContext)
        => _assemblies[assemblyKey] = standardContext;

    public bool TryGetPrefix(string prefixKey, out string? prefix, out int anchor)
    {
        if (_prefixes.TryGetValue(prefixKey, out var value))
        {
            prefix = value.Prefix;
            anchor = value.Anchor;
            return true;
        }

        prefix = null;
        anchor = 0;
        return false;
    }

    public void SetPrefix(string prefixKey, string prefix, int anchor)
        => _prefixes[prefixKey] = (prefix, anchor);

    public int Count => _nodes.Count + _assemblies.Count + _prefixes.Count;
}
