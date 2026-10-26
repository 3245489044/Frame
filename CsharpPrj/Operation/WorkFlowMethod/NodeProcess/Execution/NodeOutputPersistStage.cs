#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Runtime;
using Modules.Node.Ports;
using Modules.Pipeline;
using GraphFramework.Snapshots;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.KeySpace.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using Operation.WorkFlowMethod.Nodes.Contracts;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点即存储单元 —— 节点输出持久化 stage。
    /// 在 NodeLoop 循环体内、单节点执行步之后调用：把当前节点产出的 <see cref="PipelineContext.CurrentOutput"/>
    /// 提取为端口，写入 <see cref="INodeOutputStore"/>，使节点成为下游入参的持久化读源。
    /// 未注入 store（null）→ 直接返回，管线零回归。
    /// </summary>
    public sealed class NodeOutputPersistStage : PipelineStageBase<WfPipelineContext>
    {
        private readonly INodeOutputStore? _store;
        private readonly NodePersistCoordinator? _coordinator;

        public NodeOutputPersistStage(
            INodeOutputStore? store = null,
            NodePersistCoordinator? coordinator = null)
            : base("NodeOutputPersist", "节点即存储单元 - 持久化节点产出端口")
        {
            _store = store;
            _coordinator = coordinator;
        }

        protected override async Task ExecuteCoreAsync(WfPipelineContext context, CancellationToken cancellationToken)
        {
            if (_store is null) return; // 未装配 → 零回归
            await PersistAsync(context, _store, cancellationToken, _coordinator).ConfigureAwait(false);
        }

        /// <summary>
        /// 持久化当前节点产出：{nodeKey} + 端口 → store。
        /// 端口来源：NodeConfig.ResultKeys 定向提取；未命中配置则反射结果公共属性；两者皆空则以节点键为默认端口整存。
        /// 空 CurrentNodeKey 或空 CurrentOutput → 不写；null 端口值跳过。
        /// 注入 coordinator 时，同一 workflowId 的整批端口写经单写门（fail-fast + 有界等待）串行落盘。
        /// </summary>
        public static async Task PersistAsync(
            WfPipelineContext context,
            INodeOutputStore store,
            CancellationToken ct = default,
            NodePersistCoordinator? coordinator = null)
        {
            var nodeKey = context.CurrentNodeKey;
            if (string.IsNullOrEmpty(nodeKey)) return;

            var result = context.CurrentOutput;
            if (result is null) return;

            var snapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
            var config = snapshot?.GetNodeData(nodeKey);

            var ports = NodeResultExtractor.ExtractResultKeys(config, result);
            if (ports.Count == 0)
                ports[nodeKey] = result; // 无命名端口 → 以节点键为默认输出端口整存

            var workflowId = context.WorkflowId;

            // A5/A4：写侧唯一门 = KeyWriteGate（空键/非法键拒绝并诊断），实际落库异步提交。
            var gate = new KeyWriteGate(WorkflowKeyDiagnosticsSink.Instance);
            var writer = new NodeOutputKeyWriter();
            foreach (var (port, value) in ports)
            {
                if (value is null) continue; // 空端口不产生槽位
                var key = KeyRef.Of("workflow.node.port", PortKeySpace<object?>.BuildPortKey(nodeKey, port));
                gate.Write(writer, key, value);
            }

            async Task WriteCore()
            {
                foreach (var write in writer.Writes)
                {
                    await store.WriteOutputAsync(
                            workflowId, write.NodeKey, write.Port, write.Value, ct)
                        .ConfigureAwait(false);
                }
            }

            if (coordinator is not null)
                await coordinator.RunWriteAsync(workflowId, _ => WriteCore(), ct).ConfigureAwait(false);
            else
                await WriteCore().ConfigureAwait(false);
        }

        /// <summary>
        /// A5：<see cref="IKeyWriter"/> 的缓冲适配 —— 键校验在前（KeyWriteGate），
        /// 异步落库在后（<see cref="INodeOutputStore.WriteOutputAsync"/>），避免同步等待异步存储。
        /// </summary>
        private sealed class NodeOutputKeyWriter : IKeyWriter
        {
            public List<(string NodeKey, string Port, object? Value)> Writes { get; } = new();

            public KeyWriteResult Write(KeyRef key, object? value)
            {
                var separator = key.Value.LastIndexOf('.');
                if (separator <= 0 || separator >= key.Value.Length - 1)
                {
                    return new KeyWriteResult(
                        KeyResolution.Unparsable,
                        KeyDiagnostic.Warn(
                            KeyReasonCodes.Unparsable, KeyResolution.Unparsable, key,
                            "节点输出端口键须为 {node}.{port}"));
                }

                Writes.Add((key.Value[..separator], key.Value[(separator + 1)..], value));
                return new KeyWriteResult(KeyResolution.Resolved, null);
            }
        }
    }
}