namespace Plate.DslMethod.Abstractions;

/// <summary>绑定关系种类：Implement=代码实现 / Derive=L0/L1 契约渲染 / Guard=校验器守卫。
/// 语义对齐 dsl.md 与契约维度 ContractDimensions.Kinds（implement/derive/guard）。</summary>
public enum BindKind
{
    Implement,
    Derive,
    Guard,
}
