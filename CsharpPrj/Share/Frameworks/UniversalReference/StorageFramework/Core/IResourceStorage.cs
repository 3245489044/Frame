using System.Collections.Generic;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 资源/引用底层存储原语（内部端口）。由各后端（Sqlite/LiteDB）实现薄 DB 适配，
/// 完整端口契约与语义映射由 <see cref="StoreResourceReferenceStore"/> 在之上统一实现（SingleImplementation，对齐 TH-12）。
/// </summary>
public interface IResourceStorage
{
    void AddResource(ResourceRecord record);

    /// <summary>按 Key 更新已存在资源的可更新字段；返回是否匹配到记录（未匹配 → 由调用方回退新增）。</summary>
    bool UpdateResource(ResourceRecord record);

    ResourceRecord? GetResource(string key);

    IReadOnlyList<ResourceRecord> GetAllResources();

    void DeleteResource(string key);

    void AddReference(ReferenceRecord record);

    void RemoveReference(string sourceId, string targetId, string refType);

    /// <summary>按节点键级联删除引用（SourceId 或 TargetId == key；删除资源的引用完整性保障，消除悬空引用）。</summary>
    void RemoveReferencesByNode(string key);

    IReadOnlyList<ReferenceRecord> GetReferencesBySource(string id);

    IReadOnlyList<ReferenceRecord> GetReferencesByTarget(string id);
}
