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
/// 编译产物：指纹 + 拓扑序 wire 列表 + 违规清单。
/// <para><see cref="IsClosed"/> = 违规为空（未闭合不产出可运行 plan 的语义由调用方 Enforce 决定）。</para>
/// </summary>
public sealed record WiringPlan(
    string Fingerprint,
    IReadOnlyList<CompiledWire> Wires,
    IReadOnlyList<WiringViolation> Violations)
{
    public static WiringPlan Empty { get; } =
        new(string.Empty, Array.Empty<CompiledWire>(), Array.Empty<WiringViolation>());

    public bool IsClosed => Violations.Count == 0;

    public WiringReport Report => new(Violations);

    public CompiledWire? Find(string wireId)
    {
        foreach (var wire in Wires)
        {
            if (string.Equals(wire.Id, wireId, StringComparison.Ordinal)) return wire;
        }

        return null;
    }
}
