#region USINGS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using Operation.WorkFlowMethod.Nodes.Contracts;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Persistence
{
    /// <summary>
    /// 节点输出读视图 —— 基于 <see cref="INodeOutputStore"/> 构建入参源键空间 + 溯源。
    /// 语义：{node}.{port} 恒有；裸 {port} 仅在某端口尚未被任一更早节点输出占用时并入
    /// （防跨节点裸端口互覆盖，溯源随之归一）。源键集合与溯源键集合恒一致。
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

            var source = new Dictionary<string, object?>(StringComparer.Ordinal);
            var provenance = new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal);

            foreach (var output in outputs)
                foreach (var (port, value) in output.OutputPorts)
                {
                    if (value is null) continue; // 空端口不入源
                    var qualified = output.NodeKey + "." + port;
                    source[qualified] = value;
                    provenance[qualified] = (output.NodeKey, port);

                    if (!provenance.ContainsKey(port)) // 裸键：仅未被任一节点输出占用时并入
                    {
                        source[port] = value;
                        provenance[port] = (output.NodeKey, port);
                    }
                }

            return (source, provenance);
        }
    }
}