using Strategy.Observability.Ports;

namespace Strategy.Observability.Projection;

/// <summary>
/// 端口投影（E13）：把 <see cref="CallLog"/> 的调用记录投影为可断言视图
/// （<c>Port(...).Calls / Count / Methods / Results / Issues</c>），替代测试侧手写复合产物。
/// </summary>
public sealed class PortProjection
{
    private readonly IReadOnlyList<PortCallView> _calls;

    public PortProjection(string portKey, IEnumerable<PortCallView> calls)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey) ? throw new ArgumentException("端口键不能为空", nameof(portKey)) : portKey;
        _calls = PortGuard.Required(calls, nameof(calls)).ToArray();
    }

    /// <summary>端口键。</summary>
    public string PortKey { get; }

    /// <summary>调用记录（调用序）。</summary>
    public IReadOnlyList<PortCallView> Calls => _calls;

    /// <summary>调用次数。</summary>
    public int Count => _calls.Count;

    /// <summary>是否被调用过。</summary>
    public bool Called => _calls.Count > 0;

    /// <summary>调用序方法名（"Create" / "UpsertHistory"…）。</summary>
    public IReadOnlyList<string> Methods => _calls.Select(c => c.Method).ToArray();

    /// <summary>调用序返回值。</summary>
    public IReadOnlyList<object?> Results => _calls.Select(c => c.Result).ToArray();

    /// <summary>调用序方法名 + 实参文本（默认 <c>:</c> 分隔，如 <c>UpsertHistory:h1</c>）。</summary>
    public IReadOnlyList<string> Render(string separator = ":")
        => _calls.Select(c => c.Render(separator)).ToArray();

    /// <summary>自定义渲染（域侧只需给投影器，不需重建记录机制）。</summary>
    public IReadOnlyList<string> Render(Func<PortCallView, string> render)
    {
        PortGuard.Required(render, nameof(render));
        return _calls.Select(render).ToArray();
    }

    /// <summary>方法名 → 次数（Ordinal 键，声明序按首次出现）。</summary>
    public IReadOnlyDictionary<string, int> MethodCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in _calls)
            counts[call.Method] = counts.TryGetValue(call.Method, out var n) ? n + 1 : 1;
        return counts;
    }

    /// <summary>失败调用的错误分类清单（调用序；成功项跳过）。</summary>
    public IReadOnlyList<string> Issues
        => _calls.Where(c => !c.Succeeded).Select(c => c.ErrorClass ?? "execution_failure").ToArray();

    /// <summary>失败调用数（供 <c>issueCount</c> 断言）。</summary>
    public int IssueCount => _calls.Count(c => !c.Succeeded);

    /// <summary>实参指纹序列（同参重放识别）。</summary>
    public IReadOnlyList<string> ArgsHashes => _calls.Select(c => c.ArgsHash).ToArray();
}
