using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Diagnostics;

/// <summary>
/// 诊断码受控词表（单源）。新增码必须先在此登记（门禁 KS6）。
/// </summary>
public static class KeyReasonCodes
{
    public const string Blank = "key.blank";
    public const string Unparsable = "key.unparsable";
    public const string FamilyUnresolved = "key.family-unresolved";
    public const string FamilyAmbiguous = "key.family-ambiguous";
    public const string NotDeclared = "key.not-declared";
    public const string SetSelfOnly = "key.set-self-only";
    public const string SetEmpty = "key.set-empty";
    public const string SetCap = "key.set-cap";
    public const string RequiredMissing = "port.required-missing";
    public const string DeclaredMissing = "port.declared-missing";
    public const string WriteConflict = "key.write-conflict";
    public const string ProvisionUnattached = "key.set-provision-unattached";
    public const string ProvisionFailed = "key.set-provision-failed";

    // ---- 表示面（构造/解析/归一）----
    public const string SurfaceMismatch = "key.surface-mismatch";
    public const string EmptyIdentity = "key.empty-identity";
    public const string LegacyForm = "key.legacy-form";

    // ---- 解析内核（KPK1：候选排序/歧义/轴约束/作用域）----
    public const string ParseTrailingDot = "key.parse-trailing-dot";
    public const string ParseUnmatched = "key.parse-unmatched";
    public const string ParseAmbiguous = "key.parse-ambiguous";
    public const string AxisInvalid = "key.axis-invalid";
    public const string AxisMissing = "key.axis-missing";
    public const string ScopeMismatch = "key.scope-mismatch";

    // ---- 固有函数层（KeyOps / 键一致性门）----
    public const string OpsHostMissing = "key.ops.host-missing";
    public const string AxisNotInFamily = "key.axis-not-in-family";
    public const string PrefixSourceInvalid = "key.prefix.source-invalid";
    public const string IdentityValueMismatch = "key.identity-value-mismatch";
    public const string IdentityAxisUnknown = "key.identity-axis-unknown";
    public const string FamilyMismatch = "key.family-mismatch";

    // ---- 声明→绑定闭合（R1/R2/R3，fail-fast 结构码）----
    public const string DeclStoreMissing = "decl.store-missing";
    public const string DeclFingerprintMismatch = "decl.fingerprint-mismatch";
    public const string DeclUnbound = "decl.unbound";
    public const string DeclUnboundPort = "decl.unbound-port";
    public const string DeclOrphanImpl = "decl.orphan-impl";

    // ---- 运行期执行契约（E1–E5）----
    public const string ExecStepMissing = "exec.step-missing";
    public const string ExecPortMissing = "exec.port-missing";
    public const string ExecUndeclaredInport = "exec.undeclared-inport";
    public const string ExecUndeclaredOutport = "exec.undeclared-outport";
    public const string ExecDriverConflict = "exec.driver-conflict";
    // ---- 运行期执行契约补齐（F1/D8：失败码全表单源）----
    public const string ExecBindFailed = "exec.bind-failed";
    public const string ExecFunctionMissing = "exec.function-missing";
    public const string ExecExecutionFailure = "exec.execution-failure";
    public const string RoundCriterionUnmet = "round.criterion-unmet";
    public const string SeedCarrierMissing = "seed.carrier-missing";
    public const string SeedPortUncovered = "seed.port-uncovered";
    public const string GuardEmptyInput = "guard.empty-input";
    // 提示词前置失败（B2 一等化）：提示词端口缺失不再借 exec.port-missing 归因。
    public const string PromptMissing = "prompt.missing";

    // ---- 运行身份（R0）：身份缺失为一等失败码，不再以空作用域继续（ERR-01 / SR-04）----
    public const string IdentityMissing = "round.identity-missing";

    // ---- 种子单源（S1–S5）----
    public const string SeedEmptyInput = "seed.empty-input";
    public const string SeedSourceMissing = "seed.source-missing";
    public const string StateRootSplit = "state.root-split";

    private static readonly HashSet<string> Registered = new(StringComparer.Ordinal)
    {
        Blank, Unparsable, FamilyUnresolved, FamilyAmbiguous, NotDeclared,
        SetSelfOnly, SetEmpty, SetCap, RequiredMissing, DeclaredMissing,
        WriteConflict, ProvisionUnattached, ProvisionFailed,
        SurfaceMismatch, EmptyIdentity, LegacyForm,
        ParseTrailingDot, ParseUnmatched, ParseAmbiguous, AxisInvalid, AxisMissing, ScopeMismatch,
        OpsHostMissing, AxisNotInFamily, PrefixSourceInvalid,
        IdentityValueMismatch, IdentityAxisUnknown, FamilyMismatch,
        DeclStoreMissing, DeclFingerprintMismatch, DeclUnbound, DeclUnboundPort, DeclOrphanImpl,
        ExecStepMissing, ExecPortMissing, ExecUndeclaredInport, ExecUndeclaredOutport, ExecDriverConflict,
        ExecBindFailed, ExecFunctionMissing, ExecExecutionFailure,
        RoundCriterionUnmet, SeedCarrierMissing, SeedPortUncovered, GuardEmptyInput,
        SeedEmptyInput, SeedSourceMissing, StateRootSplit, PromptMissing, IdentityMissing,
    };

    public static bool IsRegistered(string code) => Registered.Contains(code);
}

/// <summary>空诊断汇：保留结构、不产生 IO（用于无观察者的构造场景）。</summary>
public sealed class NullKeyDiagnosticsSink : IKeyDiagnosticsSink
{
    public static readonly NullKeyDiagnosticsSink Instance = new();

    public void Report(KeyDiagnostic diagnostic)
    {
        // 显式空实现：诊断汇的 Null 形态不是「静默吞并」，而是「无观察者」。
    }
}
