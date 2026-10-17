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

    private static readonly HashSet<string> Registered = new(StringComparer.Ordinal)
    {
        Blank, Unparsable, FamilyUnresolved, FamilyAmbiguous, NotDeclared,
        SetSelfOnly, SetEmpty, SetCap, RequiredMissing, DeclaredMissing,
        WriteConflict, ProvisionUnattached, ProvisionFailed,
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
