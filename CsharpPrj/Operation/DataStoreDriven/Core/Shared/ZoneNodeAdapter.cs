using System;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 节点区适配器（唯一"业务语义 → <see cref="NodeRecord"/>"映射点，对齐公理 SingleImplementation，见 Agent/Axioms）：
/// 收敛 <see cref="NodeZoneStore"/> / <see cref="NodeConfigStore"/> / <see cref="NodeCheckpointStore"/> 各自重复的
/// NodeRecord 构造（节点键、Key/Status 语义、载荷归一化），按 <see cref="NodeTypes"/> 判别产出一致结构，
/// 消除"各用途各写一壳"的横切重复。消费方只提供业务语义，不手写 NodeRecord。
/// </summary>
public static class ZoneNodeAdapter
{
    /// <summary>zone 记录可读节点键：{nodeType}:{threadId}:{key}:{guid}。
    /// 原实现为裸 {nodeType}:{guid}（纯 Guid、无业务上下文，落库不可读）；加入 ThreadId/Key 使一眼可辨归属，Guid 保唯一。
    /// 消费方不解析该键（仅按 NodeType/ThreadId 过滤 + Id 游标），故键可安全嵌入业务上下文。</summary>
    public static string BuildNodeKey(string nodeType, string? threadId, string? key)
        => $"{nodeType}:{Normalize(threadId)}:{Normalize(key)}:{Guid.NewGuid():N}";

    /// <summary>空值归一为占位符 "-"（可读 + 保证分隔符对齐；消费方不解析键，无歧义）。</summary>
    public static string Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

    /// <summary>三区（input / output / event）节点构造：NodeKey 走 zone 风格键（{nodeType}:{threadId}:{key}:{guid}）。</summary>
    public static NodeRecord Build(string nodeType, string? threadId, string? key, string? status, string payloadJson,
        string? relJson = null, string? createdAt = null)
        => new()
        {
            NodeKey = BuildNodeKey(nodeType, threadId, key),
            NodeType = nodeType,
            ThreadId = threadId,
            Key = key,
            Status = status,
            PayloadJson = payloadJson ?? "{}",
            RelationsJson = relJson,
            CreatedAt = createdAt,
        };

    /// <summary>配置即数据行节点构造（config.{key}，Key=配置键，Payload=配置值**不透明字符串**标量）。
    /// 配置值为任意文本（非 JSON）⇒ 以 USharp 字符串标量承载，读回经 <see cref="NodeRecord.PayloadJson"/> 原样还原。</summary>
    public static NodeRecord BuildConfig(string key, string value)
        => new()
        {
            NodeKey = NodeKeyPrefix.BuildConfig(key),
            NodeType = NodeTypes.Config,
            Key = key,
            Payload = UsharpScalar.Str(value ?? string.Empty),
        };

    /// <summary>checkpoint 节点构造（checkpoint.{threadId}.{ns}.{checkpointId}，Key=&lt;ns&gt;.&lt;checkpointId&gt;，Status=Step）。</summary>
    public static NodeRecord BuildCheckpoint(string threadId, string ns, string checkpointId, string status, string payloadJson)
        => new()
        {
            NodeKey = NodeKeyPrefix.BuildCheckpoint(threadId, ns, checkpointId),
            NodeType = NodeTypes.Checkpoint,
            ThreadId = threadId,
            Key = ns + "." + checkpointId,
            Status = status,
            PayloadJson = payloadJson,
        };
}
