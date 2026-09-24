using Modules.Node.Diagnostics;

namespace Modules.Node.Identity;

/// <summary>序数分配权威（唯一作者）：键内序数段的单源（N6 / P24）。</summary>
public interface ISequenceAuthority
{
    /// <summary>分配下一个可用序数。</summary>
    bool TryNext(string sequenceKey, out long value);

    /// <summary>只读查看当前值（不推进）。</summary>
    long Peek(string sequenceKey);
}

/// <summary>未装配实现：拒绝生成含序数的键 + 可见化诊断（禁静默回退手写序号）。</summary>
public sealed class NullSequenceAuthority : ISequenceAuthority
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullSequenceAuthority(INodeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    public bool TryNext(string sequenceKey, out long value)
    {
        value = 0;
        _diagnostics.Report(new NodeDiagnostic(
            "sequence.unbound",
            NodeDiagnosticSeverity.Error,
            $"序数权威未装配，无法分配序数 '{sequenceKey}'（禁回退手写序号）",
            sequenceKey));
        return false;
    }

    public long Peek(string sequenceKey) => 0;
}

/// <summary>内存实现（测试/单进程宿主）：按序数键独立计数。</summary>
public sealed class InMemorySequenceAuthority : ISequenceAuthority
{
    private readonly Dictionary<string, long> _values = new(StringComparer.Ordinal);

    public InMemorySequenceAuthority(long start = 0) => Start = start;

    public long Start { get; }

    public bool TryNext(string sequenceKey, out long value)
    {
        _values.TryGetValue(sequenceKey, out var current);
        value = current + 1;
        _values[sequenceKey] = value;
        return true;
    }

    public long Peek(string sequenceKey) => _values.TryGetValue(sequenceKey, out var current) ? current : Start;
}

/// <summary>节点身份（N1）：键是身份本身，也是地址；位置（Placement）不参与身份。</summary>
public sealed class NodeIdentity
{
    private NodeIdentity(IdentitySpec spec, string key, IReadOnlyDictionary<string, string> values)
    {
        Spec = spec;
        Key = key;
        Values = values;
    }

    public IdentitySpec Spec { get; }

    public string Key { get; }

    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>
    /// 按规范生成身份；缺序数时经权威单源分配（未装配 → 失败 + 诊断）。
    /// 注：调用方若在 <paramref name="values"/> 里直接给出序数段，即<b>显式接管该段</b>（不再经权威分配）——
    /// 单源由调用方保证，框架不代管（N6 的例外出口，非静默回退）。
    /// </summary>
    public static bool TryFor(
        IdentitySpec spec,
        IReadOnlyDictionary<string, string> values,
        out NodeIdentity identity,
        ISequenceAuthority? sequenceAuthority = null)
    {
        var filled = new Dictionary<string, string>(values, StringComparer.Ordinal);

        foreach (var sequenceKey in spec.SequenceKeys)
        {
            if (filled.TryGetValue(sequenceKey, out var existing) && !string.IsNullOrEmpty(existing))
            {
                continue;
            }

            if (sequenceAuthority is null || !sequenceAuthority.TryNext(sequenceKey, out var next))
            {
                identity = null!;
                return false;
            }

            filled[sequenceKey] = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!KeyTemplateEvaluator.TryEvaluate(spec.Template, filled, out var key, out _))
        {
            identity = null!;
            return false;
        }

        identity = new NodeIdentity(spec, key, filled);
        return true;
    }

    public static NodeIdentity For(
        IdentitySpec spec,
        IReadOnlyDictionary<string, string> values,
        ISequenceAuthority? sequenceAuthority = null) =>
        TryFor(spec, values, out var identity, sequenceAuthority)
            ? identity
            : throw new InvalidOperationException($"身份生成失败（缺上下文值或序数权威）: {spec.Template}");

    public static bool TryParse(IdentitySpec spec, string key, out NodeIdentity identity)
    {
        if (!KeyTemplateEvaluator.TryParseValues(spec.Template, key, out var values))
        {
            identity = null!;
            return false;
        }

        identity = new NodeIdentity(spec, key, values);
        return true;
    }

    public string? Value(string name) => Values.TryGetValue(name, out var value) ? value : null;

    public override string ToString() => Key;
}
