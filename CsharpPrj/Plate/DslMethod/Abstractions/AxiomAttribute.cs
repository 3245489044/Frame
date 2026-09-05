using System;

namespace Plate.DslMethod.Abstractions;

/// <summary>
/// 公理元数据特性：标注在公理类上，供 <see cref="Logic.AxiomScanner"/> 反射索引。
/// 由 <c>AxiomCodeGenerator</c> 发射到生成的公理类；禁止手工标注在普通实现类上。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AxiomAttribute : Attribute
{
    public AxiomAttribute(AxiomLevel level, string name)
    {
        Level = level;
        Name = name;
    }

    /// <summary>公理层级。</summary>
    public AxiomLevel Level { get; }

    /// <summary>公理符号名（定义点唯一主键）。</summary>
    public string Name { get; }

    /// <summary>公理轴（boundary/round/output/infra/...，对齐 ContractDimensions.Axes）。</summary>
    public string? Axis { get; init; }

    /// <summary>公理语义说明。</summary>
    public string? Description { get; init; }
}
