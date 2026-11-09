namespace CallCenter.LlmGate.Model;

/// <summary>单次调用的门裁决。</summary>
public enum GateDecision
{
    Allow,
    Deny,
    Hold,
}

/// <summary>裁决者来源（与 <see cref="LlmCallSpan.DecidedBy"/> 对应）。</summary>
public enum DecidedBy
{
    Rule,
    Manual,
    Timeout,
    Bypass,
}

/// <summary>门模式（三值互斥）。</summary>
public static class GateModes
{
    public const string Enforce = "enforce";
    public const string Manual = "manual";
    public const string Off = "off";

    public static bool IsValid(string? mode)
        => mode is Enforce or Manual or Off;
}
