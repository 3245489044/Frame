namespace Modules.Wiring.Diagnostics;

/// <summary>
/// 结构码受控词表（单源，禁自由文本）：D1–D8 为编译期判据码，其余为声明装载码。
/// </summary>
public static class WiringReasonCodes
{
    // ---- D1–D8 编译期判据（文档 §4.5）----
    public const string WireDangling = "WIRE_DANGLING";
    public const string WireFormMismatch = "WIRE_FORM_MISMATCH";
    public const string WireSupplyMissing = "WIRE_SUPPLY_MISSING";
    public const string WireMultiWriter = "WIRE_MULTI_WRITER";
    public const string WireCycle = "WIRE_CYCLE";
    public const string WireOrphanOut = "WIRE_ORPHAN_OUT";
    public const string WirePortDrift = "WIRE_PORT_DRIFT";
    public const string WireScopeEscape = "WIRE_SCOPE_ESCAPE";

    // ---- 声明装载 / 绑定 ----
    public const string WireDeclInvalid = "WIRE_DECL_INVALID";
    public const string WireAdapterMissing = "WIRE_ADAPTER_MISSING";
    public const string WireAdapterAmbiguous = "WIRE_ADAPTER_AMBIGUOUS";
    public const string WireJudgeInvalid = "WIRE_JUDGE_INVALID";
    public const string WireFormUnsealed = "WIRE_FORM_UNSEALED";
    public const string WireDuplicateId = "WIRE_DUPLICATE_ID";
    public const string WireProfileUnknown = "WIRE_PROFILE_UNKNOWN";

    // ---- 运行期投递 ----
    public const string WireMissingRequired = "WIRE_MISSING_REQUIRED";
    public const string WireValueFormDrift = "WIRE_VALUE_FORM_DRIFT";
    public const string WireAdapterUnbound = "WIRE_ADAPTER_UNBOUND";
    public const string WireUnknownWire = "WIRE_UNKNOWN_WIRE";

    private static readonly HashSet<string> Registered = new(StringComparer.Ordinal)
    {
        WireDangling, WireFormMismatch, WireSupplyMissing, WireMultiWriter, WireCycle,
        WireOrphanOut, WirePortDrift, WireScopeEscape,
        WireDeclInvalid, WireAdapterMissing, WireAdapterAmbiguous, WireJudgeInvalid,
        WireFormUnsealed, WireDuplicateId, WireProfileUnknown,
        WireMissingRequired, WireValueFormDrift, WireAdapterUnbound, WireUnknownWire,
    };

    public static bool IsRegistered(string code) => Registered.Contains(code);

    /// <summary>全部已登记码（门禁/文档对账用）。</summary>
    public static IReadOnlyCollection<string> All => Registered;
}
