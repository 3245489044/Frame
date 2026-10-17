using Modules.KeySpace.Model;

namespace Modules.KeySpace.Abstractions;

/// <summary>键读取端口：任何读取都返回三态值。</summary>
public interface IKeyReader
{
    KeyValue<object?> Read(KeyRef key);
}

/// <summary>写结果（解析分类 + 诊断）。</summary>
public readonly record struct KeyWriteResult(KeyResolution Resolution, KeyDiagnostic? Diagnostic);

/// <summary>键写入端口。</summary>
public interface IKeyWriter
{
    KeyWriteResult Write(KeyRef key, object? value);
}

/// <summary>键分类结果（族归属 + 三态）。</summary>
public readonly record struct KeyClassification(
    KeyResolution Resolution,
    KeyRef? Key,
    KeyDiagnostic? Diagnostic);

/// <summary>字符串 → 键引用分类（唯一入口）。</summary>
public interface IKeyClassifier
{
    KeyClassification Classify(string raw);
}

/// <summary>实体判等（禁 StartsWith / 字面 ==）。</summary>
public interface IKeyEquivalence
{
    bool SameEntity(KeyRef left, KeyRef right);

    bool IsSelf(KeyRef key, KeyEntity self);
}

/// <summary>集合规划输入。EmptyPolicy 必填（无默认值）。</summary>
public readonly record struct KeySetPlanInput(
    IReadOnlyList<string> RawMembers,
    KeyEntity Self,
    int MaxTargets,
    bool AllowFanout,
    EmptySetPolicy EmptyPolicy,
    IKeySpaceDeclaration Declaration);

/// <summary>集合规划端口。</summary>
public interface IKeySetPlanner
{
    KeySetPlan Plan(KeySetPlanInput input);
}

/// <summary>端口绑定结果。</summary>
public sealed record KeyBindingResult(
    IReadOnlyDictionary<string, KeyValue<object?>> Values,
    IReadOnlyList<KeyDiagnostic> Diagnostics,
    bool RequiredMissing);

/// <summary>端口契约绑定（读/写同源）。</summary>
public interface IKeyBinder
{
    KeyBindingResult BindIn(IReadOnlyList<PortContract> ports, IKeyReader reader);

    KeyBindingResult BindOut(IReadOnlyList<PortContract> ports, IReadOnlyDictionary<string, object?> values);
}

/// <summary>诊断汇（未装配 ⇒ NullKeyDiagnosticsSink，不静默丢弃）。</summary>
public interface IKeyDiagnosticsSink
{
    void Report(KeyDiagnostic diagnostic);
}

/// <summary>键空间声明面（族 + 端口 + 策略）。</summary>
public interface IKeySpaceDeclaration
{
    string Id { get; }

    IReadOnlyList<KeyFamily> Families { get; }

    IReadOnlyList<PortContract> Ports { get; }

    IReadOnlyDictionary<string, EmptySetPolicy> Policies { get; }
}
