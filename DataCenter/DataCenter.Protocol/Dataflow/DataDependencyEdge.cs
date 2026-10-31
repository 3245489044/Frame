namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 边族与边种类取值（DP5：归属 ≠ 数据流 ≠ 顺序；一条边只能入一族）。
/// 单源：投影、查询、门禁一律引用本常量，禁各处手写字面量。
/// </summary>
public static class DataEdgeKinds
{
    /// <summary>层级包含（part-of），唯一表达归属。</summary>
    public const string StructureOwns = "structure.owns";

    /// <summary>执行顺序（来自流程步骤声明）。</summary>
    public const string ControlSequence = "control.sequence";

    /// <summary>字段级数据依赖（本框架核心边族）。</summary>
    public const string Derives = "data.derives";

    /// <summary>聚合（子层结论 → 上层结论）。</summary>
    public const string Aggregates = "data.aggregates";
}

/// <summary>
/// 字段级依赖边（DP2：边是一等公民 —— 独立 Id / 类型 / 属性，读模型中永不渲染为顶点）。
/// <para>
/// 端点闭合（C17 / <c>Check-FieldLineageClosure</c>）：<see cref="ToFields"/> 必须全部在
/// 目标产品的 <see cref="DataProductSpec.OutFields"/> 中声明，否则为越权写。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.3/§3.4。
/// </summary>
/// <param name="EdgeId">边规范键（层内唯一；确定性形态 <c>{from}|{kind}|{to}</c>，带标签时为 <c>{from}|{kind}|{to}|{label}</c>）。</param>
/// <param name="FromProductId">源产品。</param>
/// <param name="FromFields">源字段路径（字段级血缘的起点；空 = 产品级依赖）。</param>
/// <param name="ToProductId">目标产品。</param>
/// <param name="ToFields">目标字段路径（须 ⊆ 目标产品出字段）。</param>
/// <param name="EdgeKind">边种类（<see cref="DataEdgeKinds"/>）。</param>
/// <param name="Assertion">断言强度（必填，DP4）。</param>
/// <param name="Label">边标签（同一 (端点, 边族) 上的多重关系区分键；null = 无标签，边键形态保持旧口径）。</param>
/// <param name="Properties">边属性（声明面结构事实，键序无语义；null = 无属性）。</param>
public sealed record DataDependencyEdge(
    string EdgeId,
    string FromProductId,
    IReadOnlyList<string> FromFields,
    string ToProductId,
    IReadOnlyList<string> ToFields,
    string EdgeKind,
    DependencyAssertion Assertion,
    string? Label = null,
    IReadOnlyDictionary<string, string?>? Properties = null);
