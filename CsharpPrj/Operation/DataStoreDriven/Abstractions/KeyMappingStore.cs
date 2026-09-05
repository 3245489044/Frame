using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>KeyMapping 分类（KeyMapping 属于节点逻辑，SQL 存储应分类）：
    /// 节点入参映射 / 节点出参映射。</summary>
    public enum KeyMappingCategory
    {
        NodeInput,
        NodeOutput,
    }

    /// <summary>
    /// KeyMapping 存储端口 —— KeyMapping 属于节点逻辑，以 SQL 分类存储（category + key → valueJson）。
    /// 与图库中的 KeyMapping↔节点 关系互补：SQL 存映射内容（逻辑），图存映射与节点的关系（拓扑）。
    /// </summary>
    public interface IKeyMappingStore
    {
        Task InitializeAsync(CancellationToken ct = default);

        Task SaveAsync(KeyMappingCategory category, string key, string valueJson, CancellationToken ct = default);
    }
}
