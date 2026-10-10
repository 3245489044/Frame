namespace Modules.Node.Relations;

/// <summary>
/// 一条静态引用关系 —— 原始 <c>(SourceResourceType, SourceId, TargetResourceType, TargetId, RefType)</c> 三元组，
/// 对齐上层引用库「AddReferenceAsync」写入面。用于工作流「组合 + 控制流」边
/// （<c>Owns</c>：工作流包含步骤；<c>Calls</c>：上一步骤调用下一步骤）等
/// **不落入** <see cref="NodeRelationType"/> 两类节点关系（KeyMappingToNode / NodeToNode）的静态引用。
/// 与节点关系（<see cref="NodeRelation"/>）并存、互不干扰，经上层引用守卫幂等写入引用图，供图算法/影响分析消费。
/// </summary>
public sealed record StaticReference(
    string SourceResourceType,
    string SourceId,
    string TargetResourceType,
    string TargetId,
    string RefType,
    double Strength = 1.0);
