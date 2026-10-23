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

    // ---- 种子单源（S1–S5）----
    public const string SeedEmptyInput = "seed.empty-input";
    public const string SeedSourceMissing = "seed.source-missing";
    public const string StateRootSplit = "state.root-split";

    private static readonly HashSet<string> Registered = new(StringComparer.Ordinal)
    {
        Blank, Unparsable, FamilyUnresolved, FamilyAmbiguous, NotDeclared,
        SetSelfOnly, SetEmpty, SetCap, RequiredMissing, DeclaredMissing,
        WriteConflict, ProvisionUnattached, ProvisionFailed,
        DeclStoreMissing, DeclFingerprintMismatch, DeclUnbound, DeclUnboundPort, DeclOrphanImpl,
        ExecStepMissing, ExecPortMissing, ExecUndeclaredInport, ExecUndeclaredOutport, ExecDriverConflict,
        SeedEmptyInput, SeedSourceMissing, StateRootSplit,
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
