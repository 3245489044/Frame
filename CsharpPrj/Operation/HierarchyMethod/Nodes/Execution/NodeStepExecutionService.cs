using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.HierarchyMethod.Center;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Relations;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.LoopFoldMethod.Reference.Gateways;
using Operation.WorkFlowMethod.Nodes.Contracts;
using ParameterResolver.KeyMapping;
using Share.HierarchySupport;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.KeyMapping;

using Modules.Pipeline.StepExec;

namespace Operation.HierarchyMethod.Nodes.Execution
{
    /// <summary>
    /// 节点执行服务 —— 一切逻辑以 workflow 节点为主：
    /// 把 <see cref="RoundStep"/> 构建为 <see cref="NodeDefinition"/>（存储 + 执行单元），
    /// 经 KeyMapping 从「数据空间 + 其他节点输出端口」解析入参、调用注册函数、
    /// 把结果作为该节点的输出端口持久化到 <see cref="INodeOutputStore"/>（节点为存储基底）。
    /// 增强路径：KeyMapping（节点逻辑）分类写 SQL（<see cref="IKeyMappingStore"/>）；
    /// 节点关系（业务节点写透镜像 + KeyMapping↔节点 / 节点↔节点边）经引用自动落地守卫
    /// （<see cref="ReferenceGrounding"/> + <see cref="WriteGuardPort"/>，不手写建边）。
    /// </summary>
    public sealed class NodeStepExecutionService
    {
        private readonly INodeOutputStore? _store;
        private readonly NodeReadView? _readView;
        private readonly INodeStore? _nodes;
        private readonly IKeyMappingStore? _keyMappings;
        private readonly Func<CancellationToken, Task<ITransaction>>? _beginTransaction;
        private readonly ReferenceGrounding? _grounding;
        private readonly NodeRelationGateway? _relationGateway;
        // 归属边簿记流入口（R1）：task→node（cross_db_link）的唯一编排触发点；null ⇒ 迁移期直调回退（见字段声明处退役条件）。
        private readonly Operation.HierarchyMethod.Nodes.Linkage.INodeAttributionFlow? _attributionFlow;
        private readonly string _workflowId;
        // 阶段二：函数调用端口（解耦 WorkFlowMethod/WorkflowContext 直调；默认 canonical 兜底，行为与旧直调一致）。
        private readonly INodeFunctionInvoker _functionInvoker;
        // R3 单一确定性写门：业务节点持久化的唯一汇合点（同步路径与 Outbox 后台共用本单例实例）串行单写 + 有界等待。
        private readonly WriteCoordinator _writeCoordinator;
        // DP-3（W2-0）双写收敛开关（默认关）：开 = 执行期不再扁平写数据空间，读侧改由 ProjectOutputsAsync 自权威投影重算。
        private readonly bool _unifiedStateWrite;
        // 运行时 AssertSingleWriter 注册键：业务节点持久化写目标与其唯一权威写者（固定名，平行实现注册同目标 → fail-fast）。
        private const string PersistWriteTarget = "ur_nodes.persist";
        private const string PersistWriterName = "NodeStepExecutionService.PersistAsync";

        // 诊断日志插槽（缺省 no-op）：由组合根回填具体日志门面（分层无环——框架不感知 Agent 诊断类型）。
        private readonly Action<string, object?>? _log;

        public NodeStepExecutionService(
            INodeOutputStore? store,
            string workflowId = NodeKeys.DefaultWorkflowId,
            INodeStore? nodeStore = null,
            IKeyMappingStore? keyMappings = null,
            Func<CancellationToken, Task<ITransaction>>? beginTransaction = null,
            ReferenceGrounding? grounding = null,
            INodeFunctionInvoker? functionInvoker = null,
            WriteCoordinator? writeCoordinator = null,
            bool unifiedStateWrite = false,
            Action<string, object?>? log = null,
            Operation.HierarchyMethod.Nodes.Linkage.INodeAttributionFlow? attributionFlow = null)
        {
            _store = store;
            // R4 读源收敛：task 作用域单一读源视图（NodeReadView），见 EnumerateInputOutputsAsync——本方法经 ReadView 枚举，
            // 取代「ur_nodes 全量 + 引用图解析」跨库拼读直写；诊断边界（图读 vs ur_nodes 读）经显式解析器重载保留。
            _readView = store != null ? new NodeReadView(store) : null;
            _nodes = nodeStore;
            _keyMappings = keyMappings;
            _beginTransaction = beginTransaction;
            _grounding = grounding;
            // 引用写（权威边 + task 归属边）直连 Reference 门面 NodeRelationGateway；未装配守卫 → null（引用写 no-op）
            _relationGateway = grounding is null ? null : new NodeRelationGateway(grounding, store: null);
            _attributionFlow = attributionFlow;
            _workflowId = workflowId;
            _functionInvoker = functionInvoker ?? new WorkflowMethodFunctionInvoker(workflowId); // R2 canonical：Center 唯一实现
            // R3：写门缺省自建单实例（同步路径与 Outbox 后台共享本服务实例 → 同门串行）；组合根可注入共享实例并注册 AssertSingleWriter。
            _writeCoordinator = writeCoordinator ?? new WriteCoordinator();
            _unifiedStateWrite = unifiedStateWrite;
            _log = log;
            // 运行时 AssertSingleWriter（SEP.DeterministicCoreDisjoint）：声明本服务为业务节点持久化的唯一确定性写路径。
            // 固定写者名 → 同目标多实例（直跑/单测）幂等不炸；出现不同写者平行注册同目标才 fail-fast（平行实现违反单一写者）。
            SingleWriteGuard.AssertSingleWriter(PersistWriteTarget, PersistWriterName);
        }

        public string WorkflowId => _workflowId;

        /// <summary>把声明式 <see cref="RoundStep"/> 构建为 <see cref="NodeDefinition"/>（节点即存储+执行单元）。
        /// 节点主键 = 稳定 NodeId（<see cref="RoundStep.EffectiveNodeId"/>），而非 FuncName —— 同名函数多次出现
        /// （如 collect-analyze-act 的 cline_execute×2）时不再同名覆盖；FuncName 仍保留供按名调用注册函数。</summary>
        public static NodeDefinition BuildNode(RoundStep step, string workflowId = NodeKeys.DefaultWorkflowId)
        {
            var p = RoundStepMapping.Project(step);
            return new NodeDefinition
            {
                NodeKey = p.NodeKey,
                WorkflowId = workflowId,
                FuncName = p.FuncName,
                Symbol = p.NodeKey,
                CallKey = p.FuncName,
                ParameterKeys = p.ParameterKeys,
                ResultKeys = p.ResultKeys,
                InputMapping = p.InputMapping,
                OutputMapping = p.OutputMapping,
            };
        }

        /// <summary>
        /// 解析节点入参：数据空间快照 + 全节点输出端口 → 经 KeyMapping 映射到节点 ParameterKeys。
        /// 返回入参字典 + 溯源（哪些入参来自哪些上游节点输出端口，供图 NodeToNode 组合边持久化）。
        /// </summary>
        public async Task<NodeInputSnapshot> ResolveInputsAsync(
            NodeDefinition node, IDataSpace vars, string? taskKey = null, KeyMappingConfig? globalMapping = null, CancellationToken ct = default)
        {
            var source = new Dictionary<string, object?>(StringComparer.Ordinal);
            var sourceNode = new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal);

            // ① 节点输出端口优先（节点即存储单元）：{node}.{port} 恒有；
            //    裸 {port} 仅在尚未被任一节点输出占用时加入，避免跨节点裸端口互覆盖（溯源随之填充，保证 NodeToNode 边一致）。
            //    枚举源可过滤：传 taskKey 走过滤枚举（task 作用域寻址，只取本 task 归属节点），否则全量枚举（兼容）。
            if (_store != null)
            {
                var outputs = await EnumerateInputOutputsAsync(taskKey, ct).ConfigureAwait(false);
                foreach (var output in outputs)
                    foreach (var (port, value) in output.OutputPorts)
                    {
                        if (value is null) continue;
                        var qual = $"{output.NodeKey}.{port}";
                        source[qual] = value;
                        sourceNode[qual] = (output.NodeKey, port);
                        if (!sourceNode.ContainsKey(port))
                        {
                            source[port] = value;
                            sourceNode[port] = (output.NodeKey, port);
                        }
                    }
            }

            // ② 数据空间兜底：仅填充未被节点输出占用的键（节点输出优先于数据空间，消除裸端口与数据空间键冲突导致的溯源缺失）。
            foreach (var kv in vars.Snapshot())
                if (kv.Value != null && !source.ContainsKey(kv.Key))
                    source[kv.Key] = kv.Value;

            var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
            var provenance = new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal);
            foreach (var key in node.ParameterKeys)
                if (source.TryGetValue(key, out var v) && v != null)
                {
                    parameters[key] = v!;
                    if (sourceNode.TryGetValue(key, out var pn)) provenance[key] = pn;
                }

            var merged = KeyMappingMerge.MergeMappings(node.InputMapping, globalMapping);
            if (node.ParameterKeys.Count > 0 && merged.Rules.Count > 0 && source.Count > 0)
            {
                var (_, matches) = KeyProcessor.Mapper.MapBatch(source.Keys.ToList(), node.ParameterKeys.ToList(), merged);
                foreach (var m in matches)
                    if (!parameters.ContainsKey(m.TargetKey) &&
                        source.TryGetValue(m.SourceKey, out var mv) && mv != null)
                    {
                        parameters[m.TargetKey] = mv;
                        if (sourceNode.TryGetValue(m.SourceKey, out var pn)) provenance[m.TargetKey] = pn;
                    }
            }

            return new NodeInputSnapshot(parameters, provenance);
        }

        /// <summary>
        /// 输入枚举源（全量 / 过滤两级并存，过滤为更高抽象层，对齐 MCP「复合节点引用关系」task 作用域寻址）：
        /// 传 taskKey 且装配引用守卫时，按 cross_db_link 边只取本 task 归属节点输出，根除跨 task 污染；
        /// 无 taskKey / 无守卫 → 回退全量枚举（现有行为，兼容直跑/单测）。过滤枚举结果为空（如首轮尚无归属边）→ 空源。
        /// </summary>
        private async Task<IReadOnlyList<NodeOutput>> EnumerateInputOutputsAsync(string? taskKey, CancellationToken ct)
        {
            if (_readView is null) return Array.Empty<NodeOutput>(); // 未装配节点输出存储 → 空源
            // 诊断：进入读源边界（若卡死在任一存储读，此条在而后续某条缺失 → 精确定位读锁归属）。
            _log?.Invoke("NodeStepExecutionService_ResolveInputs_enumerateStart", new { taskKey = taskKey ?? "", workflowId = _workflowId });
            // 显式解析器（R4 读源经 NodeReadView）：仅在「有 taskKey 且装配引用守卫」时提供，内部在图读后打点
            // （缺此条 = 卡图读 GetTaskNodeKeysAsync）；无 taskKey / 无守卫 → 传 null → ReadView 回退全量（语义与既有兼容）。
            Func<string, string, CancellationToken, Task<IReadOnlyCollection<string>>>? resolver =
                _grounding is null ? null : async (tk, wf, t) =>
                {
                    var keys = await _grounding!.GetTaskNodeKeysAsync(tk, wf, t).ConfigureAwait(false);
                    _log?.Invoke("NodeStepExecutionService_ResolveInputs_graphKeysDone", new { taskKey = tk, keyCount = keys.Count });
                    return keys;
                };
            var outputs = await _readView.ListTaskOutputsAsync(_workflowId, taskKey, resolver, ct).ConfigureAwait(false);
            // 诊断：SQLite ur_nodes 读（经 ReadView）已返回；此条缺失 = 卡 ur_nodes 读 EnumerateOutputsAsync。
            _log?.Invoke("NodeStepExecutionService_ResolveInputs_urNodesDone", new { workflowId = _workflowId, count = outputs.Count });
            return outputs;
        }

        /// <summary>
        /// 执行节点（纯函数 + 内存写，D2 解耦持久化）：调用注册函数（统一经 <see cref="INodeFunctionInvoker"/>）→
        /// 提取结果端口 → 扁平兼容写入数据空间（原键，P3）。**不做任何落库**——持久化由 <see cref="PersistAsync"/> 在步骤后横切执行。
        /// </summary>
        public async Task<Dictionary<string, object?>> ExecuteAsync(
            NodeDefinition node, IDataSpace vars, NodeInputSnapshot input, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 阶段二：函数调用统一经 INodeFunctionInvoker（解耦 WorkFlowMethod/WorkflowContext 直调），参数经 FunctionContext 语义。
            // Dictionary 为不变泛型，Dictionary<string,object> 不能赋给 Dictionary<string,object?> → 用 ToDictionary 显式投影为可空值域。
            var parameters = input.Parameters.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
            // 唯一调用走廊（9.3 §三 / S3 目标入口）：handler(InvokeAsync) 经 StepExec 顺序中间件栈执行 ——
            // Observability（invokeStart/funcReturned 观测+计时，I2 横切收敛，同 msgId/字段/时序 → 行为等价，
            // 并保持「有 invokeStart 缺 funcReturned = 卡 handler 内部」的卡死诊断判定口径）→ StepTimeout（I3 单步超时，
            // ctx.StepTimeout 未置位 = 直跑）。后续横切（LLM 门/资源预热/错误归一）可插拔加入，不改调用方。
            var result = await StepExec.InvokeAsync(
                new StepExecContext(node.FuncName, node.NodeKey),
                token => _functionInvoker.InvokeAsync(node.FuncName, parameters, token),
                ct,
                new[]
                {
                    // Observability 经日志插槽回填组合根日志门面（框架内核零 Agent 日志依赖；logKey 归一为两次打点前缀）。
                    StepExecMiddlewares.Observability("NodeStepExecutionService_ExecuteAsync", (key, data) => _log?.Invoke(key, data)),
                    StepExecMiddlewares.StepTimeout(),
                }).ConfigureAwait(false);

            var props = ExtractProps(node, result);

            // 扁平兼容写入数据空间（供后续步骤直接读原键；P3 拷贝语义）。
            // DP-3（开关开）：停写 —— 同一事实单写 ur_nodes（权威），读侧由 ProjectOutputsAsync 自权威回填。
            if (!_unifiedStateWrite)
            {
                foreach (var key in node.ResultKeys)
                    if (props.TryGetValue(key, out var v))
                        vars.Set(key, v);
            }

            return props;
        }

        /// <summary>
        /// 投影重算（DP-3 收敛点 1，仅开关开时消费）：自权威（ur_nodes 节点输出端口）读回本节点产出，
        /// 回填数据空间扁平键 —— 使读侧（后续步骤 / 轮内消费者）不依赖执行期扁平写入，写侧保持单写。
        /// 未装配存储 / 读回为空 / 值解码失败 → 静默跳过（读侧回退既有语义，不阻断执行）。
        /// </summary>
        public async Task ProjectOutputsAsync(NodeDefinition node, IDataSpace vars, CancellationToken ct)
        {
            if (!_unifiedStateWrite || _store is null) return;
            var output = await _store.TryGetNodeOutputAsync(_workflowId, node.NodeKey, ct).ConfigureAwait(false);
            if (output is null) return;
            foreach (var key in node.ResultKeys)
                if (output.OutputPorts.TryGetValue(key, out var v) && v is not null)
                    vars.Set(key, v);
        }

        /// <summary>
        /// 持久化节点输出（步骤后横切后置，D2 解耦）：全部结果以节点输出端口持久化（ur_nodes，节点为基底）
        /// + 节点元数据落盘（KeyMapping 分类写 SQL + 引用层自动落地图关系）。
        /// 由 <see cref="RoundStepInvoker.PersistStepAsync"/> 经 StaticAsyncPipeline.onAfterStep 调用；
        /// 失败由横切层单独归类、不翻转业务 ok。
        /// </summary>
        public Task PersistAsync(
            NodeDefinition node, IReadOnlyDictionary<string, object?> props, NodeInputSnapshot input, string? taskKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // R3 单一确定性写门：业务节点持久化（ur_nodes 输出 + KeyMapping 分类写 + 引用边）全经 WriteCoordinator 串行单写——
            // 同步路径（OnAfterStepAsync）与 Outbox 后台共用同一写门，杜绝跨路径并发写持锁/读-改-写竞态（锁卡死 → StoreTimeoutException 自证）。
            return _writeCoordinator.RunWriteAsync(
                $"persist:{node.NodeKey}",
                t => PersistCoreAsync(node, props, input, taskKey, t),
                ct: ct);
        }

        /// <summary>
        /// 节点输出端口单点写入口（非步骤路径复用：终态信封 guard_output/cline_execute 补写）。
        /// 与 <see cref="PersistAsync"/> 共用同一 <see cref="WriteCoordinator"/> 实例与目标命名（persist:{nodeKey}），
        /// 消除调用方直写 <see cref="INodeOutputStore"/> 的旁路（DP-1 / SEP.DeterministicCoreDisjoint：同一事实单一写路径）；
        /// 空值守卫仍由存储层统一承担。未装配节点输出存储 → 不阻断（增强路径可空）。
        /// </summary>
        public Task WritePortAsync(string nodeKey, string port, object? value, CancellationToken ct = default)
        {
            if (_store is null) return Task.CompletedTask;
            return _writeCoordinator.RunWriteAsync(
                $"persist:{nodeKey}",
                t => WritePortCoreAsync(nodeKey, port, value, t),
                ct: ct);
        }

        private Task WritePortCoreAsync(string nodeKey, string port, object? value, CancellationToken ct)
            => _store!.WriteOutputAsync(_workflowId, nodeKey, port, value, ct);

        /// <summary>
        /// 轮次产物整批落库（非步骤路径复用，CP-1 配套）：会话/折叠链路无步骤暂存（<see cref="NodeRoundContext"/>），
        /// 只持有轮次产出快照 <see cref="NodeExecutionRecord"/> 列表 —— 逐节点写输出端口（ur_nodes，经同一
        /// <see cref="WriteCoordinator"/> 单写门与 <c>persist:{nodeKey}</c> 目标命名）+ 业务节点行 + 引用/归属边。
        /// <para>与 <see cref="PersistAsync"/> 共用同一写门（禁第二写路径）；输出端口写失败单独归类、不吞（由上抛由调用方裁决）。</para>
        /// <para>端口写循环与步骤路径（<see cref="WriteNodeOutputsAsync"/>）**语义不同故不合并**：步骤路径的输出端口 + KeyMapping
        /// 并入同一 SQLite 事务（见 <see cref="PersistNodeOutputsAndKeyMappingsAsync"/>，需直写），本路径无事务、逐端口经写门单写。</para>
        /// </summary>
        /// <param name="records">本轮各步产出快照（NodeKey 空者跳过；无任何非空产出端口者跳过）。</param>
        /// <param name="taskKey">task→node 归属边键（空 = 不建归属边）。</param>
        /// <param name="ct">取消令牌。</param>
        public async Task PersistRoundAsync(
            IReadOnlyList<NodeExecutionRecord> records, string? taskKey = null, CancellationToken ct = default)
        {
            if (records is null) return;
            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(record.NodeKey)) continue;
                // 空端口守卫（E5）：无任何非空产出的记录不落库 —— 不造「无产出空节点」（空结果语义见 InitialStateNullity）。
                if (record.OutputPorts is not { Count: > 0 } || !record.OutputPorts.Any(kv => kv.Value is not null)) continue;

                foreach (var (port, value) in record.OutputPorts)
                    if (value is not null)
                        await WritePortAsync(record.NodeKey, port, value, ct).ConfigureAwait(false);

                // 业务节点行 + 引用/归属边：以最小 NodeDefinition 复用引用落地实现（不复制建边逻辑）。
                if (_nodes is null && _relationGateway is null) continue;
                var node = new NodeDefinition
                {
                    NodeKey = record.NodeKey,
                    WorkflowId = _workflowId,
                    Symbol = record.NodeKey,
                    ResultKeys = record.OutputPorts.Keys.ToList(),
                };
                var input = new NodeInputSnapshot(
                    new Dictionary<string, object?>(StringComparer.Ordinal), record.Provenance);
                await PersistNodeReferencesAsync(node, input, taskKey, ct).ConfigureAwait(false);
            }
        }

        private async Task PersistCoreAsync(
            NodeDefinition node, IReadOnlyDictionary<string, object?> props, NodeInputSnapshot input, string? taskKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // R4 收敛（同库事务合并）：节点输出端口写（ur_nodes NodeType=output）+ KeyMapping 分类写（SQL）
            // 并入同一 SQLite 事务一次 commit——消除"产出落库成功但映射失败"的中间态，ur_nodes 侧原子。
            // 注入事务工厂（AgentOrchestrator 注入 BeginTransactionAsync，返回非空 ITransaction）时包事务；未注入 → 逐条直写。
            await PersistNodeOutputsAndKeyMappingsAsync(node, props, ct).ConfigureAwait(false);

            // 引用层自动落地（跨介质，无单库原子性）：业务节点写透镜像（wf.node）+ 边推导统一写入，最终一致 + 对账兜底。
            if (_nodes != null || _grounding != null)
                await PersistNodeReferencesAsync(node, input, taskKey, ct).ConfigureAwait(false);
        }

        /// <summary>节点输出端口写 + KeyMapping 分类写（同库 agent.db）并入同一 SQLite 事务一次 commit（消除 R4 事务碎片）。</summary>
        private async Task PersistNodeOutputsAndKeyMappingsAsync(
            NodeDefinition node, IReadOnlyDictionary<string, object?> props, CancellationToken ct)
        {
            var hasOutputs = _store != null && props.Any(kv => kv.Value != null);
            var hasMappings = _keyMappings != null && (node.InputMapping != null || node.OutputMapping != null);
            if (!hasOutputs && !hasMappings) return;

            if (_beginTransaction is not null)
            {
                var tx = await _beginTransaction(ct).ConfigureAwait(false);
                if (tx is not null)
                {
                    await using (tx)
                    {
                        if (hasOutputs) await WriteNodeOutputsAsync(node, props, ct).ConfigureAwait(false);
                        if (hasMappings) await SaveKeyMappingsAsync(node, ct).ConfigureAwait(false);
                        await tx.CommitAsync().ConfigureAwait(false);
                    }
                    return;
                }
            }
            // 未注入事务工厂 / 工厂返回 null → 逐条直写（兼容无持久化装配路径）
            if (hasOutputs) await WriteNodeOutputsAsync(node, props, ct).ConfigureAwait(false);
            if (hasMappings) await SaveKeyMappingsAsync(node, ct).ConfigureAwait(false);
        }

        /// <summary>节点即存储单元：全部结果以节点输出端口持久化（可按节点检索复用）。</summary>
        private async Task WriteNodeOutputsAsync(NodeDefinition node, IReadOnlyDictionary<string, object?> props, CancellationToken ct)
        {
            if (_store == null) return;
            foreach (var (port, value) in props)
                if (value != null)
                    await _store.WriteOutputAsync(node.WorkflowId, node.NodeKey, port, value, ct).ConfigureAwait(false);
        }

        private async Task SaveKeyMappingsAsync(NodeDefinition node, CancellationToken ct)
        {
            var key = KeyMappingKeys.NodeLevelKey(_workflowId, node.NodeKey);
            if (node.InputMapping != null)
                await _keyMappings!.SaveAsync(KeyMappingCategory.NodeInput, key, JsonSerializer.Serialize(node.InputMapping), ct).ConfigureAwait(false);
            if (node.OutputMapping != null)
                await _keyMappings!.SaveAsync(KeyMappingCategory.NodeOutput, key, JsonSerializer.Serialize(node.OutputMapping), ct).ConfigureAwait(false);
        }

        /// <summary>引用层自动落地：业务节点写透基础节点库（NodeType=node，携带关系状态 RelationsJson）→ 经守卫写入权威边集。
        /// 「有守卫走守卫、无守卫跳过」由装配期注入决定，不依赖具体装饰器类型（WriteGuardPort）；守卫 reconcile 幂等，
        /// 即便节点库自身已写透对账也无副作用（收敛到同一权威边集）。引用写（②③）收敛至 Agent.Reference 门面。</summary>
        private async Task PersistNodeReferencesAsync(NodeDefinition node, NodeInputSnapshot input, string? taskKey, CancellationToken ct)
        {
            var hasMapping = node.InputMapping != null || node.OutputMapping != null;
            var relationsJson = NodeRelationGateway.SerializeRelations(hasMapping, input.Provenance);
            var edges = NodeRelationGateway.DeriveEdgesForNode(_workflowId, node.NodeKey, hasMapping, input.Provenance);

            // ① 业务节点 → 基础节点库（NodeType=node，携带关系状态）：存储侧留 Core；写透守卫（WriteGuardPort）PutAsync 后
            //    自动镜像内容跟随（wf.node 图资源，upsert）+ 依 RelationsJson 对账收敛边；原始 INodeStore 则仅落节点库。
            //    F-63 形态事实（取证）：ur_nodes 业务节点为**单形** —— NodeType=node + BusinessNodePayload{Symbol, Outputs}，
            //    输出端口由 NodeOutputStore.WriteOutputAsync 内嵌写、读侧 EnumerateOutputsAsync 亦只枚举 NodeType=node；
            //    本方法读-改-写**只改** Symbol/RelationsJson 并原样保留既有 Outputs（DecodeBusiness→EncodeBusiness）。
            //    残余口径缺口（非本处）：API node.write 缺省形可为 NodeType=output 且载荷非 NodeOutputDoc
            //    （见 AgentApiIntentSource.ToRecord），形态收敛另立编号，不在此处改语义。
            if (_nodes != null)
            {
                var existing = await _nodes.GetAsync(node.NodeKey, ct).ConfigureAwait(false);
                var payload = existing != null
                    ? global::Operation.HierarchyMethod.Nodes.NodeOutputCodec.DecodeBusiness(existing.PayloadJson)
                    : new global::Operation.HierarchyMethod.Nodes.BusinessNodePayload();
                payload.Symbol = node.Symbol;
                await _nodes.PutAsync(new NodeRecord
                {
                    NodeKey = node.NodeKey,
                    NodeType = NodeTypes.Node,
                    ThreadId = _workflowId,
                    Status = "executed",
                    PayloadJson = global::Operation.HierarchyMethod.Nodes.NodeOutputCodec.EncodeBusiness(payload),
                    RelationsJson = relationsJson,
                }, ct).ConfigureAwait(false);
            }

            // ② 权威边集写入 + ③ task 归属边：引用侧直连 NodeRelationGateway（未装配守卫 → no-op）。
            //    ② 当节点库为写透守卫（WriteGuardPort）时，PutAsync 已依 RelationsJson 完成图镜像 + 权威边推导（单点，
            //    不再重复建边）；仅当节点库非守卫但注入引用守卫时，门面显式写权威边保持等价（防御性回退）。
            //    ③ task 归属边为独立加法路径（不并入数据流权威边集，避免删残留误删本归属边）；taskKey 缺失跳过。
            if (_relationGateway is not null)
            {
                await _relationGateway.WriteRelationsAsync(
                    _workflowId, node.NodeKey, edges, isWriteThroughStore: _nodes is WriteGuardPort, ct).ConfigureAwait(false);
            }

            // ③ task 归属边（cross_db_link）：R1 归流 —— 经**归属边簿记流入口**触发（编排真源 =
            //    Config/workflows/attribution.ushar 的 [Flow] NodeAttribution；本处只是唯一触发点，不持编排）。
            //    未装配入口（迁移期：既有装配切片未提供）⇒ 沿用直调回退，不静默跳过；
            //    退役条件 = 全部装配切片提供 INodeAttributionFlow 后删除本回退支。
            if (_attributionFlow is not null && !string.IsNullOrWhiteSpace(taskKey))
            {
                await _attributionFlow.AttachAsync(taskKey!, _workflowId, node.NodeKey, ct).ConfigureAwait(false);
            }
            else if (_relationGateway is not null)
            {
                await _relationGateway.EnsureTaskNodeLinkAsync(taskKey, _workflowId, node.NodeKey, ct).ConfigureAwait(false);
            }
        }

        /// <summary>提取结果端口：注册了特化的强类型执行结果按 ResultKeys 整对象定向全写；其余按公开属性名反射约定。</summary>
        public static Dictionary<string, object?> ExtractProps(NodeDefinition node, object? result)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            // 执行器返回 null → 无产出端口（空结果，对齐 InitialStateNullity/RepeatedCallStable 语义；避免 result.GetType() NRE）。
            if (result is null) return props;
            // 已注册特化（NodeResultExtractor.RegisterSpecialized；如 Agent 组合根注册的强类型执行结果）→
            // 按 ResultKeys 整对象定向全写，消除「ResultKeys 与返回类型属性名不一致 → 结果键静默丢失」的反射错配。
            if (NodeResultExtractor.HasSpecialized(result.GetType()))
                return NodeResultExtractor.ExtractResultKeys(node.ResultKeys, result);
            foreach (var p in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                props[p.Name] = p.GetValue(result);
            return props;
        }
    }
}
