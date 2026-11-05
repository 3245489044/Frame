namespace Modules.KeySpace.Model;

/// <summary>解析结果分类（唯一 reason 语义来源）。</summary>
public enum KeyResolution { Resolved, Missing, Empty, Unparsable, Ambiguous, NotDeclared, Mismatched }

/// <summary>三态值状态：Known / Unknown / NotApplicable（禁 default/空串冒充）。</summary>
public enum KeyValueState { Known, Unknown, NotApplicable }

public enum KeyDiagnosticSeverity { Info, Warning, Error }

public enum PortDirection { In, Out }

/// <summary>
/// 空集策略：必填，不许「空集 = 无事发生」。
/// </summary>
public enum EmptySetPolicy { Fail, CreateMember, PassThrough, Report }

/// <summary>结构化诊断（reason code 单源 = KeyReasonCodes）。</summary>
public sealed record KeyDiagnostic(
    string Code,
    KeyDiagnosticSeverity Severity,
    KeyResolution Resolution,
    KeyRef? Key,
    string Message)
{
    public static KeyDiagnostic Warn(string code, KeyResolution resolution, KeyRef? key, string message)
        => new(code, KeyDiagnosticSeverity.Warning, resolution, key, message);

    public static KeyDiagnostic Info(string code, KeyResolution resolution, KeyRef? key, string message)
        => new(code, KeyDiagnosticSeverity.Info, resolution, key, message);

    public static KeyDiagnostic Error(string code, KeyResolution resolution, KeyRef? key, string message)
        => new(code, KeyDiagnosticSeverity.Error, resolution, key, message);
}

/// <summary>
/// 三态值：Known / Unknown / NotApplicable。消费方必须先看 State，再取值。
/// </summary>
public readonly record struct KeyValue<T>(KeyValueState State, T? Value, KeyDiagnostic? Diagnostic)
{
    public bool HasValue => State == KeyValueState.Known;

    public static KeyValue<T> Known(T value) => new(KeyValueState.Known, value, null);

    public static KeyValue<T> Unknown(string code, string message, KeyRef key)
        => new(KeyValueState.Unknown, default, KeyDiagnostic.Warn(code, KeyResolution.Missing, key, message));

    public static KeyValue<T> NotApplicable(string code, string message, KeyRef key)
        => new(KeyValueState.NotApplicable, default, KeyDiagnostic.Info(code, KeyResolution.NotDeclared, key, message));
}

/// <summary>
/// 集合规划结果：目标 + 理由 + 策略 + 分类丢弃计数（无「裸数组」出口）。
/// </summary>
public sealed record KeySetPlan(
    IReadOnlyList<KeyRef> Targets,
    string Reason,
    EmptySetPolicy Policy,
    int DroppedBlank,
    int DroppedSelf,
    int DroppedDuplicate,
    int DroppedCap,
    int Unparsable)
{
    public bool IsEmpty => Targets.Count == 0;

    /// <summary>是否可继续（空集但策略为 PassThrough/Report 时仍可继续，但必须可见）。</summary>
    public bool Satisfied => !IsEmpty || Policy is EmptySetPolicy.PassThrough or EmptySetPolicy.Report;

    public static KeySetPlan Empty(string reason, EmptySetPolicy policy)
        => new(Array.Empty<KeyRef>(), reason, policy, 0, 0, 0, 0, 0);
}

/// <summary>端口契约：声明面 → 运行时绑定。Required 缺失必须可见失败。</summary>
public sealed record PortContract(
    string Name,
    PortDirection Direction,
    string FamilyId,
    bool Required,
    string? DefaultValue,
    KeyCardinality Cardinality);
