using Modules.Wiring.Diagnostics;
using Modules.Wiring.Runtime;

namespace Modules.Wiring.Compile;

/// <summary>编译后的单条 wire：声明 + 已解析两端 + 委托（运行期零查找、零反射）。</summary>
public sealed record CompiledWire(
    LinkedWire Link,
    Func<ValueEnvelope, ValueTask<WireResult>> Route)
{
    public string Id => Link.Id;

    public WireSpecAccessor Spec => new(Link.Spec.Id, Link.From.Ref, Link.To.Ref, Link.From.Form, Link.To.Form);
}

/// <summary><see cref="CompiledWire"/> 的只读坐标视图（诊断/记账用结构事实）。</summary>
public readonly record struct WireSpecAccessor(string Id, string From, string To, string FromForm, string ToForm);

/// <summary>
/// 编译产物：指纹 + 拓扑序 wire 列表 + 违规清单 + 运行期索引（ById/BySource/RequiredInputs）。
/// <para><see cref="IsClosed"/> = 违规为空（未闭合不产出可运行 plan 的语义由调用方 Enforce 决定）。</para>
/// </summary>
public sealed record WiringPlan(
    string Fingerprint,
    IReadOnlyList<CompiledWire> Wires,
    IReadOnlyList<WiringViolation> Violations,
    IReadOnlyDictionary<string, CompiledWire>? IndexById = null,
    IReadOnlyDictionary<string, IReadOnlyList<CompiledWire>>? IndexBySource = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? RequiredInputs = null)
{
    private static readonly IReadOnlyDictionary<string, CompiledWire> EmptyById =
        new Dictionary<string, CompiledWire>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CompiledWire>> EmptyBySource =
        new Dictionary<string, IReadOnlyList<CompiledWire>>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyRequired =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public static WiringPlan Empty { get; } =
        new(string.Empty, Array.Empty<CompiledWire>(), Array.Empty<WiringViolation>(), EmptyById, EmptyBySource, EmptyRequired);

    public bool IsClosed => Violations.Count == 0;

    public WiringReport Report => new(Violations);

    /// <summary>wireId → 编译 wire（运行期 O(1)）。</summary>
    public IReadOnlyDictionary<string, CompiledWire> ById => IndexById ?? BuildById(Wires);

    /// <summary>源端口 → 出边列表（编译期构建；顺序 = 拓扑序内声明序，可复算）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CompiledWire>> BySource
        => IndexBySource ?? BuildBySource(Wires);

    /// <summary>盒 → 必填输入端口坐标清单（handler 前置供给闸门用）。</summary>
    public IReadOnlyList<string> RequiredFor(string boxId)
        => RequiredInputs is not null && RequiredInputs.TryGetValue(boxId, out var ports)
            ? ports
            : Array.Empty<string>();

    public CompiledWire? Find(string wireId)
        => ById.TryGetValue(wireId, out var wire) ? wire : null;

    /// <summary>源端口扇出（无出边 ⇒ 空列表，由调用方给出 WIRE_ORPHAN_SOURCE）。</summary>
    public IReadOnlyList<CompiledWire> From(string sourceRef)
        => BySource.TryGetValue(sourceRef, out var wires) ? wires : Array.Empty<CompiledWire>();

    /// <summary>构造单源入口：索引在此一次建好并与 wire 顺序同源。</summary>
    public static WiringPlan Create(
        string fingerprint,
        IReadOnlyList<CompiledWire> wires,
        IReadOnlyList<WiringViolation> violations,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? requiredInputs = null)
        => new(fingerprint, wires, violations,
            BuildById(wires), BuildBySource(wires),
            requiredInputs ?? EmptyRequired);

    private static IReadOnlyDictionary<string, CompiledWire> BuildById(IReadOnlyList<CompiledWire> wires)
    {
        var index = new Dictionary<string, CompiledWire>(wires.Count, StringComparer.Ordinal);
        foreach (var wire in wires) index[wire.Id] = wire;
        return index;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CompiledWire>> BuildBySource(IReadOnlyList<CompiledWire> wires)
    {
        var index = new Dictionary<string, List<CompiledWire>>(StringComparer.Ordinal);
        foreach (var wire in wires)
        {
            var source = wire.Link.From.Ref;
            if (!index.TryGetValue(source, out var edges))
            {
                edges = new List<CompiledWire>();
                index[source] = edges;
            }

            edges.Add(wire);
        }

        var frozen = new Dictionary<string, IReadOnlyList<CompiledWire>>(index.Count, StringComparer.Ordinal);
        foreach (var pair in index) frozen[pair.Key] = pair.Value.ToArray();
        return frozen;
    }
}
