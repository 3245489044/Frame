using Plate.DslMethod.Abstractions;

namespace Plate.DslMethod.Model;

/// <summary>绑定来源通道：Code=编译期 [BindAxiom] 特性（当前唯一通道）。</summary>
public enum BindingChannel
{
    Code,
}

/// <summary>扫描出的公理类元数据（来自 [Axiom] 特性）。契约源文件映射集中登记于生成产物的 mapping-report.txt，不在类型上重复。</summary>
public sealed record AxiomInfo(
    Type AxiomType,
    string Name,
    AxiomLevel Level,
    string Axis,
    string? Description);

/// <summary>一个实现类与公理之间的绑定（来自 [BindAxiom] 特性 / markdown [Bind]）。</summary>
public sealed record AxiomBindingInfo(
    Type? DeclaringType,
    Type? AxiomType,
    string AxiomName,
    AxiomLevel? Level,
    string Axis,
    BindKind Kind,
    BindStatus Status,
    BindingChannel Channel);
