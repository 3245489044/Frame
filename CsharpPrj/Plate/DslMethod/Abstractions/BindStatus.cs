namespace Plate.DslMethod.Abstractions;

/// <summary>绑定状态：Aligned=已对齐 / Gap=缺公理 / Drift=漂移。
/// 语义对齐 dsl.md 与契约维度 ContractDimensions.Statuses（aligned/gap/drift）。</summary>
public enum BindStatus
{
    Aligned,
    Gap,
    Drift,
}
