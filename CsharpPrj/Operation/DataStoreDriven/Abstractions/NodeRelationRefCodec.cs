using System;
using Modules.Node.Relations;
using ReferenceTypes = Operation.DataStoreDriven.Abstractions.ReferenceTypes;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 节点关系 refType 编解码 —— **唯一实现**（A2）。
/// <para>
/// 自 <c>Operation.DataStoreDriven.Graph.ReferenceGraphCoordinator</c> 的私有 <c>Ref</c> 类上移为公开单源：
/// 私有常量无公开源导致抽象层只能另写字面量（旧缺陷 A-2），此处公开后
/// <see cref="ReferenceTypes.NodeToNode"/> / <see cref="ReferenceTypes.KeyMappingToNode"/> 与编码前缀**同源**。
/// </para>
/// <para>
/// 落点说明（与方案 §2.3-A2 的差异）：方案原文拟置于抽象层 <c>LoopFoldMethod.Reference</c>，但
/// <c>ReferenceGraphCoordinator</c> 在本层（原语层）⇒ 本层调用上层构成工程环；故编解码随写边 owner 同层公开。
/// </para>
/// <para>纪律：refType 文本的一切解析/构造只经本类；协调器与本层之外禁止再写字面解析逻辑（TH-12）。</para>
/// </summary>
public static class NodeRelationRefCodec
{
    /// <summary>节点关系 → refType 文本（NodeToNode 带端口 + 可选 km；KeyMappingToNode 为裸类型名）。</summary>
    /// <param name="relation">节点关系。</param>
    public static string Encode(NodeRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        return relation.Type switch
        {
            NodeRelationType.NodeToNode =>
                $"{ReferenceTypes.NodeToNode}:{relation.SourcePort ?? string.Empty}>{relation.TargetPort ?? string.Empty}" +
                (relation.KeyMappingRef is null ? string.Empty : $"?km={relation.KeyMappingRef}"),
            NodeRelationType.KeyMappingToNode => ReferenceTypes.KeyMappingToNode,
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation.Type, "未知节点关系类别"),
        };
    }

    /// <summary>按 refType 前缀判定节点关系类别（D3 收口：协调器解析唯一入口，非两类 ⇒ null）。</summary>
    /// <param name="refType">refType 文本。</param>
    public static NodeRelationType? ParseType(string refType)
    {
        if (refType is null) return null;
        if (refType.StartsWith(ReferenceTypes.KeyMappingToNode, StringComparison.Ordinal)) return NodeRelationType.KeyMappingToNode;
        if (refType.StartsWith(ReferenceTypes.NodeToNode, StringComparison.Ordinal)) return NodeRelationType.NodeToNode;
        return null;
    }

    /// <summary>解析 NodeToNode 形式的 refType（非该前缀 ⇒ 三元组全 null）。</summary>
    /// <param name="refType">refType 文本。</param>
    public static (string? FromPort, string? ToPort, string? KeyMappingRef) ParseNodeToNode(string refType)
    {
        if (refType is null || !refType.StartsWith(ReferenceTypes.NodeToNode, StringComparison.Ordinal))
            return (null, null, null);
        var body = refType.Substring(ReferenceTypes.NodeToNode.Length).TrimStart(':');
        string? km = null;
        var qIdx = body.IndexOf('?');
        if (qIdx >= 0)
        {
            var q = body.Substring(qIdx + 1);
            body = body.Substring(0, qIdx);
            if (q.StartsWith("km=", StringComparison.Ordinal)) km = q.Substring(3);
        }
        var gIdx = body.IndexOf('>');
        if (gIdx < 0) return (null, null, km);
        return (Body(body.Substring(0, gIdx)), Body(body.Substring(gIdx + 1)), km);
    }

    private static string? Body(string s) => string.IsNullOrEmpty(s) ? null : s;
}
