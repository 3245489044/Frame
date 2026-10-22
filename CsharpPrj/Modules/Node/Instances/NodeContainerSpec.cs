using Modules.Node.Diagnostics;
using Modules.Node.Identity;

namespace Modules.Node.Instances;

/// <summary>
/// 容器声明（域无关）：容器 Kind + 身份模板 + 持有子 Kind + 保留上限 + 是否运行态面。
/// <para>
/// 容器成员由引用边推导，**不冗余存储**；级联只沿 <see cref="HoldsKinds"/>（对齐 <c>Owns</c> 语义）。
/// </para>
/// </summary>
/// <param name="Kind">容器类别（如 history/task；词面由宿主声明）。</param>
/// <param name="IdentityTemplate">容器身份模板（键文法内核求值）。</param>
/// <param name="HoldsKinds">持有（可级联）子实例/节点类别集。</param>
/// <param name="MaxRetained">保留上限（可空 = 不限制）。</param>
/// <param name="RuntimeSurface">是否有运行态内存面。</param>
public sealed record NodeContainerSpec(
    string Kind,
    string IdentityTemplate,
    IReadOnlyList<string>? HoldsKinds = null,
    int? MaxRetained = null,
    bool RuntimeSurface = false)
{
    /// <summary>持有类别集（去 null，空集非 null）。</summary>
    public IReadOnlyList<string> Holds => HoldsKinds ?? Array.Empty<string>();

    /// <summary>容器身份规范（模板非法 ⇒ 抛，装配期 fail-fast）。</summary>
    public IdentitySpec CreateIdentity() => IdentitySpec.Create(IdentityTemplate);
}

/// <summary>
/// 容器注册表（实例级注入；装配期登记、运行期只读）：同 Kind 重复 ⇒ fail-fast。
/// </summary>
public sealed class NodeContainerRegistry
{
    private readonly Dictionary<string, NodeContainerSpec> _specs = new(StringComparer.Ordinal);
    private readonly INodeDiagnosticsSink _diagnostics;
    private bool _frozen;

    /// <param name="specs">容器声明集（可空 = 空注册表）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public NodeContainerRegistry(
        IEnumerable<NodeContainerSpec>? specs = null,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        foreach (var spec in specs ?? Array.Empty<NodeContainerSpec>())
        {
            Register(spec);
        }
    }

    /// <summary>是否已冻结（运行期只读门）。</summary>
    public bool IsFrozen => _frozen;

    /// <summary>全部容器声明（序 = Kind 序数序）。</summary>
    public IReadOnlyList<NodeContainerSpec> All
        => _specs.Values.OrderBy(static spec => spec.Kind, StringComparer.Ordinal).ToArray();

    /// <summary>登记容器（重复 Kind / 空 Kind / 空模板 ⇒ 抛；冻结后登记 ⇒ 抛）。</summary>
    public void Register(NodeContainerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (_frozen)
        {
            throw new InvalidOperationException("容器注册表已冻结：运行期禁止登记");
        }

        if (string.IsNullOrWhiteSpace(spec.Kind) || string.IsNullOrWhiteSpace(spec.IdentityTemplate))
        {
            throw new InvalidOperationException("容器声明须含非空 Kind 与 IdentityTemplate");
        }

        if (!_specs.TryAdd(spec.Kind, spec))
        {
            throw new InvalidOperationException($"容器 Kind 重复：{spec.Kind}（每 Kind 恰一个声明）");
        }
    }

    /// <summary>冻结（此后只读）。</summary>
    public void Freeze() => _frozen = true;

    /// <summary>按 Kind 取声明（未登记 ⇒ false + 可见化）。</summary>
    public bool TryGet(string? kind, out NodeContainerSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(kind) && _specs.TryGetValue(kind, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        _diagnostics.Report(new NodeDiagnostic(
            "container.unregistered",
            NodeDiagnosticSeverity.Warning,
            "容器 Kind 未声明：按空容器处理（不静默造声明）",
            kind ?? string.Empty));
        return false;
    }

    /// <summary>按 Kind 取声明；未登记 ⇒ 抛。</summary>
    public NodeContainerSpec Require(string kind)
        => _specs.TryGetValue(kind, out var spec)
            ? spec
            : throw new KeyNotFoundException($"未登记的容器 Kind '{kind}'");

    /// <summary>某容器直接持有的子 Kind 集（未登记 ⇒ 空集 + 诊断）。</summary>
    public IReadOnlyList<string> HoldsOf(string kind)
        => TryGet(kind, out var spec) ? spec.Holds : Array.Empty<string>();
}
