using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Runtime;

/// <summary>
/// 对消费方的唯一门面：分类、规划、绑定、读取各恰一个入口。
/// 必填依赖缺失即构造期抛出（不静默降级）。
/// </summary>
public sealed class KeySpaceFacade
{
    private readonly IKeyClassifier _classifier;
    private readonly IKeyEquivalence _equivalence;
    private readonly IKeySetPlanner _planner;
    private readonly IKeyBinder _binder;
    private readonly IKeyDiagnosticsSink _diagnostics;

    public KeySpaceFacade(
        KeySpaceDeclaration declaration,
        IKeyClassifier classifier,
        IKeyEquivalence equivalence,
        IKeySetPlanner planner,
        IKeyBinder binder,
        IKeyDiagnosticsSink diagnostics)
    {
        Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _equivalence = equivalence ?? throw new ArgumentNullException(nameof(equivalence));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public KeySpaceDeclaration Declaration { get; }

    public IKeyEquivalence Equivalence => _equivalence;

    public KeyClassification Classify(string raw) => _classifier.Classify(raw);

    public KeySetPlan PlanSet(KeySetPlanInput input) => _planner.Plan(input);

    public KeyBindingResult BindIn(IReadOnlyList<PortContract> ports, IKeyReader reader)
        => _binder.BindIn(ports, reader);

    public KeyBindingResult BindOut(IReadOnlyList<PortContract> ports, IReadOnlyDictionary<string, object?> values)
        => _binder.BindOut(ports, values);

    /// <summary>读取（三态原样返回；诊断旁路到汇）。</summary>
    public KeyValue<object?> Read(IKeyReader reader, KeyRef key)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var value = reader.Read(key);
        if (value.Diagnostic is not null)
        {
            _diagnostics.Report(value.Diagnostic);
        }

        return value;
    }
}

/// <summary>
/// 写侧唯一门（单写者语义）：空键拒绝；写结果非 Resolved ⇒ 诊断可见。
/// </summary>
public sealed class KeyWriteGate
{
    private readonly IKeyDiagnosticsSink _diagnostics;

    public KeyWriteGate(IKeyDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullKeyDiagnosticsSink.Instance;

    public KeyWriteResult Write(IKeyWriter writer, KeyRef key, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (key.IsEmpty)
        {
            var diagnostic = KeyDiagnostic.Error(
                KeyReasonCodes.WriteConflict, KeyResolution.Empty, key, "空键禁止写入");
            _diagnostics.Report(diagnostic);
            return new KeyWriteResult(KeyResolution.Empty, diagnostic);
        }

        var result = writer.Write(key, value);
        if (result.Resolution != KeyResolution.Resolved && result.Diagnostic is not null)
        {
            _diagnostics.Report(result.Diagnostic);
        }

        return result;
    }
}
