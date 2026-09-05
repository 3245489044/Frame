namespace Operation.LoopFoldMethod.Reference.Model;

/// <summary>
/// 一条引用边（Reference 抽象的最小边模型）：源 → [RefType] → 目标。
/// 仅描述「实体间存在哪种引用关系」，写边原语仍留 DataStoreDriven 图协调器（TH-12）；
/// 本模型供 <see cref="IHierarchyContainer{T}"/> 出边与纯推演（Derivation）消费，不承载写边逻辑。
/// </summary>
/// <param name="RefType">边类型，取值见 <see cref="ReferenceTypes"/>（唯一常量源）。</param>
/// <param name="SourceId">源端点键（如 taskKey / wf.node）。</param>
/// <param name="TargetId">目标端点键。</param>
public sealed record ReferenceEdge(string RefType, string SourceId, string TargetId);
