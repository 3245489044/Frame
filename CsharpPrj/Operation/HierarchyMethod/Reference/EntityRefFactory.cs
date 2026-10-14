using System;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 三层实体定位工厂（LLM-free）——把「kind 词（history|task|node）+ id（+ node 的 workflowId）」解析为
/// <see cref="EntityRef"/> 的**单一实现**，消除各消费方重复的 kind→EntityKind 映射与 EntityRef 构造。
/// <para>仅做映射与构造，不产出诊断文案（文案归调用方，保持各入口文案逐字不变）。</para>
/// </summary>
public static class EntityRefFactory
{
    /// <summary>kind 是否为本框架认可的三层实体词（history|task|node，大小写不敏感）。</summary>
    public static bool IsKnownKind(string? kind)
        => TryParseKind(kind, out _);

    /// <summary>解析 kind 词 → <see cref="EntityKind"/>（非法返回 false）。</summary>
    public static bool TryParseKind(string? kind, out EntityKind entityKind)
    {
        switch (kind?.Trim().ToLowerInvariant())
        {
            case "history": entityKind = EntityKind.History; return true;
            case "task": entityKind = EntityKind.Task; return true;
            case "node": entityKind = EntityKind.Node; return true;
            default: entityKind = default; return false;
        }
    }

    /// <summary>
    /// 解析 (kind, id, workflowId) → <see cref="EntityRef"/>。id 空 / kind 非法 ⇒ false。
    /// node 携带 <paramref name="workflowId"/>（缺省由 <see cref="EntityRef.Normalize"/> 归一为默认工作流）。
    /// </summary>
    public static bool TryCreate(string? kind, string? id, string? workflowId, out EntityRef entity)
    {
        entity = default;
        if (string.IsNullOrWhiteSpace(id) || !TryParseKind(kind, out var entityKind)) return false;
        entity = entityKind == EntityKind.Node
            ? new EntityRef(EntityKind.Node, id!.Trim(), workflowId)
            : new EntityRef(entityKind, id!.Trim());
        return true;
    }
}
