namespace DataCenter.Protocol.Standards;

/// <summary>端口入参字段声明（L0.5 标准模型的最小字段项）。</summary>
public sealed record PortFieldStandard(string Name, string Type, bool Required);

/// <summary>
/// 端口标准项（跨语言、语言无关、无领域词汇）：<c>ports.json</c> 的 <c>ports[x]</c> 的中立形态。
/// <para>
/// 与内核 <c>BoundaryContract</c> 的关系（只加不改）：本模型是**声明式中立形态**，
/// 由 L1 装配器映射为内核契约（<c>PortStandard → BoundaryContract</c>）；本层**不引内核类型**。
/// </para>
/// <para>
/// <see cref="Impl"/> 自 B3 起为**结构化标准模型**（不再是 RawText 字符串）：
/// 标准层因此可独立回答「这个端口怎么被装配、脚本会做什么、投影取哪些字段」。
/// </para>
/// </summary>
public sealed record PortStandard(
    string Key,
    IReadOnlyList<PortFieldStandard> Inputs,
    IReadOnlyList<string> OutFields,
    IReadOnlyList<string> Depends,
    string Scope,
    int Version,
    IReadOnlyList<string> Emits,
    PortImplStandard? Impl);
