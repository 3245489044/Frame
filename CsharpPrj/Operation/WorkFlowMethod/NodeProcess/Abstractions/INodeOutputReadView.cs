#region USINGS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Abstractions
{
    /// <summary>
    /// 节点输出读视图 —— 把「节点输出存储」投影为节点执行入参解析的源键空间与溯源。
    /// 语义：<c>{node}.{port}</c> 恒有；裸 <c>{port}</c> 仅在尚未被任一节点输出占用时并入（防跨节点裸端口互覆盖）。
    /// 溯源（<see cref="BuildProvenanceAsync"/>）记录「键 ← 源节点输出端口」，供图 NodeToNode 组合边持久化。
    /// 与现有 <c>IParameterResolver</c>/<c>KeyProcessor</c> 衔接：节点端口 ∪ 数据空间 → 参数键。
    /// </summary>
    public interface INodeOutputReadView
    {
        /// <summary>构建入参源键空间：{node}.{port} 恒有；裸 {port} 仅未被任一节点输出占用时并入。</summary>
        Task<IReadOnlyDictionary<string, object?>> BuildInputSourceAsync(string workflowId, CancellationToken ct = default);

        /// <summary>构建溯源映射：键 → 源节点输出端口（键集合与 <see cref="BuildInputSourceAsync"/> 一致）。</summary>
        Task<IReadOnlyDictionary<string, (string Node, string Port)>> BuildProvenanceAsync(string workflowId, CancellationToken ct = default);
    }
}