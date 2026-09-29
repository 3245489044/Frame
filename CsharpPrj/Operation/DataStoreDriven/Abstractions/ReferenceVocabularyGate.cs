namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 引用词表校验姿态（B3 词表武装）：写边时对「未在词表 DSL 声明的边类型」的处置方式。
/// <para>迁移期默认 <see cref="Report"/>（不拒写，仅可见化）；待 DSL 覆盖 ⊇ 实际边集后切 <see cref="Deny"/>。</para>
/// <para>可见化出口复用 <see cref="IReferenceEdgeDiagnostics"/>（同一判据族：边类型未在声明面登记）。</para>
/// </summary>
public enum ReferenceVocabularyGate
{
    /// <summary>报告模式（默认，迁移期）：未声明也放行，仅经诊断出口可见化（不拒写）。</summary>
    Report = 0,

    /// <summary>拒绝模式：未声明 ⇒ 拒写 + 可见化（禁静默建边）。</summary>
    Deny = 1,
}
