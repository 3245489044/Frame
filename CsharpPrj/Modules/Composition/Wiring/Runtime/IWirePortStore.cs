namespace Modules.Wiring.Runtime;

/// <summary>
/// 端口值仓：<c>(scope, portRef) → ValueEnvelope</c>。写入即供给，命令 handler 读仓而非回读原始载荷。
/// <para>scope 只接受 <see cref="WireScopeRouter"/> 的合法形态（非法即显式失败，禁冒充 global）。</para>
/// </summary>
public interface IWirePortStore
{
    void Set(string scope, string portRef, ValueEnvelope value);

    bool TryGet(string scope, string portRef, out ValueEnvelope value);

    bool Has(string scope, string portRef);

    void Clear(string scope);
}

/// <summary>内存端口值仓（无宿主依赖；scope → portRef → value 单写者受锁保护）。</summary>
public sealed class InMemoryWirePortStore : IWirePortStore
{
    private readonly Dictionary<string, Dictionary<string, ValueEnvelope>> _scopes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Set(string scope, string portRef, ValueEnvelope value)
    {
        if (!WireScopeRouter.TryResolve(scope, out var instance))
            throw new ArgumentException("非法 scope（须 global 或 kind:key）: " + scope, nameof(scope));
        if (string.IsNullOrWhiteSpace(portRef))
            throw new ArgumentException("portRef 不可为空", nameof(portRef));

        lock (_gate)
        {
            if (!_scopes.TryGetValue(instance.Scope, out var ports))
            {
                ports = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
                _scopes[instance.Scope] = ports;
            }

            ports[portRef] = value;
        }
    }

    public bool TryGet(string scope, string portRef, out ValueEnvelope value)
    {
        value = default;
        if (!WireScopeRouter.TryResolve(scope, out var instance) || string.IsNullOrWhiteSpace(portRef)) return false;

        lock (_gate)
        {
            return _scopes.TryGetValue(instance.Scope, out var ports) && ports.TryGetValue(portRef, out value);
        }
    }

    public bool Has(string scope, string portRef)
        => TryGet(scope, portRef, out var value) && value.HasPayload;

    public void Clear(string scope)
    {
        if (!WireScopeRouter.TryResolve(scope, out var instance)) return;
        lock (_gate) _scopes.Remove(instance.Scope);
    }
}
