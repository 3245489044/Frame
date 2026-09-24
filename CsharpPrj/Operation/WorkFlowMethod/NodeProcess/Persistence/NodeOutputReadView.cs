#region USINGS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Ports;
using Modules.Node.Projection;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using Operation.WorkFlowMethod.Nodes.Contracts;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Persistence
{
    /// <summary>
    /// 节点输出读视图 —— 基于 <see cref="INodeOutputStore"/> 构建入参源键空间 + 溯源。
    /// 语义：{node}.{port} 恒有；裸 {port} 仅在某端口尚未被任一更早节点输出占用时并入
    /// （防跨节点裸端口互覆盖，溯源随之归一）。源键集合与溯源键集合恒一致。
    /// 键空间规则唯一实现 = <see cref="PortKeySpace{TValue}"/>（读写同一实现，G2）：
    /// 枚举序 = 键序数序（先按 <see cref="NodeKeyOrdinalComparer"/> 排序），"先到者胜"不得随载体枚举序漂移。
    /// </summary>
    public sealed class NodeOutputReadView : INodeOutputReadView
    {
        private readonly INodeOutputStore _store;

        public NodeOutputReadView(INodeOutputStore store) => _store = store;

        public async Task<IReadOnlyDictionary<string, object?>> BuildInputSourceAsync(string workflowId, CancellationToken ct = default)
        {
            var (source, _) = await BuildAsync(workflowId, ct).ConfigureAwait(false);
            return source;
        }

        public async Task<IReadOnlyDictionary<string, (string Node, string Port)>> BuildProvenanceAsync(string workflowId, CancellationToken ct = default)
        {
            var (_, provenance) = await BuildAsync(workflowId, ct).ConfigureAwait(false);
            return provenance;
        }

        private async Task<(IReadOnlyDictionary<string, object?> Source,
                             IReadOnlyDictionary<string, (string Node, string Port)> Provenance)> BuildAsync(
            string workflowId, CancellationToken ct)
        {
            var outputs = await _store.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);

            // 枚举序 = 键序数序（N6）：裸键"先到者胜"按键序数序判定，禁随载体枚举序漂移。
            var space = new PortKeySpace<object?>();

            foreach (var output in outputs.OrderBy(output => output.NodeKey, NodeKeyOrdinalComparer.Instance))
                foreach (var (port, value) in output.OutputPorts)
                {
                    space.TryAdd(output.NodeKey, port, value);     // {node}.{port} 恒有
                    space.TryAddBare(output.NodeKey, port, value); // 裸键：未被任一更早节点占用时并入
                }

            var provenance = new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal);
            foreach (var entry in space.Entries)
            {
                provenance[entry.PortKey] = (entry.NodeKey, entry.Port);
            }

            return (space.Snapshot(), provenance);
        }
    }
}