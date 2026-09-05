using System;

namespace Plate.DslMethod.Abstractions;

/// <summary>
/// 实现类 ↔ 公理绑定特性（编译期、推荐主通道）。标注在 Agent.Core 实现类上，
/// 建立「代码实现 ↔ 公理」的显式、可机检绑定；由 <see cref="Logic.AxiomScanner"/> 扫描。
/// 可多实例（一个类绑定多条公理，如 LoopDecisionMapper → EvaluationOutcomeSet + ClassifyError）。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BindAxiomAttribute : Attribute
{
    public BindAxiomAttribute(Type axiomType, BindKind kind = BindKind.Implement, BindStatus status = BindStatus.Aligned)
    {
        AxiomType = axiomType;
        Kind = kind;
        Status = status;
    }

    /// <summary>被绑定的公理类型（<see cref="IAxiom"/> 实现类）。</summary>
    public Type AxiomType { get; set; }

    /// <summary>绑定关系种类。</summary>
    public BindKind Kind { get; set; }

    /// <summary>绑定状态。</summary>
    public BindStatus Status { get; set; }
}
