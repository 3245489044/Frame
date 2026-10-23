using Modules.Node.Ports;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>存在性口径单源：null 是否算“给出”、空白串是否算“给出”。</summary>
public sealed record PresenceSemantics(
    PortPresence NullAs = PortPresence.ExplicitNull,
    PortPresence BlankAs = PortPresence.Value)
{
    /// <summary>契约域：null = 显式空（由规则 + 可空声明裁决），空白串 = 有值。</summary>
    public static PresenceSemantics ContractDefault { get; } = new();

    /// <summary>提示词前置域：null 与空白均视为缺失（保留既有语义）。</summary>
    public static PresenceSemantics PromptPreflight { get; } = new(PortPresence.Absent, PortPresence.Absent);
}
