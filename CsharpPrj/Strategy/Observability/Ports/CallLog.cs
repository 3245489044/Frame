using System.Globalization;

namespace Strategy.Observability.Ports;

/// <summary>
/// 一次端口调用的记录视图（E10）：出证的最小单元。
/// <see cref="Method"/> 默认取端口键末段（端口键形态 = 接口/类型.方法），可显式指定。
/// </summary>
public sealed record PortCallView(
    string PortKey,
    string Method,
    IReadOnlyList<object?> Args,
    string ArgsHash,
    string? Src,
    bool Succeeded,
    string? ErrorClass,
    double ElapsedMs,
    object? Result,
    DateTimeOffset AtUtc)
{
    /// <summary>端口键末段（"A.B.C" → "C"；无点则原样）。</summary>
    public static string MethodOf(string portKey)
    {
        var index = portKey.LastIndexOf('.');
        return index >= 0 && index < portKey.Length - 1 ? portKey[(index + 1)..] : portKey;
    }

    /// <summary>调用文本渲染（默认 <c>方法[:实参…]</c>），与期望值中的字符串形态对齐。</summary>
    public string Render(string separator = ":")
    {
        if (Args.Count == 0) return Method;
        return Method + separator + string.Join(separator, Args.Select(Text));
    }

    /// <summary>不变量文本化（null→空串；bool→True/False；数值→不变量格式）——单源 = <see cref="PortValues.TextInvariant"/>。</summary>
    public static string Text(object? value) => PortValues.TextInvariant(value) ?? string.Empty;
}

/// <summary>
/// 调用记录（E10）：端口调用序与实参的<b>单一记录点</b>（装配期注入，装饰器写、投影读）。
/// 线程安全；保留全量（有界窗口由消费方决定，不在此截断）。
/// </summary>
public sealed class CallLog
{
    private readonly List<PortCallView> _calls = new();
    private readonly object _gate = new();

    /// <summary>追加一条调用记录。</summary>
    public void Record(PortCallView call)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (_gate) _calls.Add(call);
    }

    /// <summary>全量快照（调用序）。</summary>
    public IReadOnlyList<PortCallView> Snapshot()
    {
        lock (_gate) return _calls.ToList();
    }

    /// <summary>指定端口的调用快照（调用序）。</summary>
    public IReadOnlyList<PortCallView> For(string portKey)
    {
        lock (_gate) return _calls.Where(c => string.Equals(c.PortKey, portKey, StringComparison.Ordinal)).ToList();
    }

    /// <summary>调用次数（<paramref name="portKey"/> 为 null 时统计全部）。</summary>
    public int Count(string? portKey = null)
    {
        lock (_gate)
        {
            return portKey is null
                ? _calls.Count
                : _calls.Count(c => string.Equals(c.PortKey, portKey, StringComparison.Ordinal));
        }
    }

    /// <summary>全部被调用过的端口键（Ordinal 升序，去重）。</summary>
    public IReadOnlyList<string> PortKeys()
    {
        lock (_gate) return _calls.Select(c => c.PortKey).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>调用序方法名（不做实参渲染；需要实参用 <see cref="Snapshot"/>）。</summary>
    public IReadOnlyList<string> MethodNames()
    {
        lock (_gate) return _calls.Select(c => c.Method).ToArray();
    }

    /// <summary>清空（测试隔离用）。</summary>
    public void Clear()
    {
        lock (_gate) _calls.Clear();
    }
}
