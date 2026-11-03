using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Share.Foundations.Diagnostics;

using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Linkage;
using Operation.WorkFlowMethod.Nodes.Contracts;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Execution;
using Share.CircuitFramework.Projection;
using Share.Conversation.Aggregates;
using Share.Conversation.Commands;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using Share.TaskStatusPool.TaskPool;
using UniversalWorkflowFramework.Relations.Mounting;

namespace Operation.HierarchyMethod.Tasks.Orchestration;

/// <summary>
/// 主循环编排端口（纯逻辑、零 LLM）：管理 task 状态 + 编排 nodes；不跑 LLM（LLM 工作全在 cline 子循环）。
/// 只依赖纯逻辑端口（<see cref="IContainerMemberPort"/> / <see cref="INodeOutputStore"/> / <see cref="ITaskLifecyclePort"/>），
/// 未装配返回 no-op/空集（对齐「未装配返回空集诊断」，不抛、不阻断）。
/// </summary>
public interface ITaskOrchestrator
{
    /// <summary>建 task 并挂到 history（history→task 归属；taskKey 幂等）。</summary>
    Task CreateTaskAsync(string historyId, string taskKey, string? input = null, CancellationToken ct = default);

    /// <summary>
    /// 建**成员** task（空集策略 CreateMember 的唯一落地口）：history 归属 + 子智能体标签/执行器写入 + 激活。
    /// </summary>
    Task<bool> CreateMemberTaskAsync(
        string historyId, string taskKey, string? input,
        string? subAgentTag, string? executorId, CancellationToken ct = default);

    /// <summary>激活 task（→Running，触发子循环）。</summary>
    Task ActivateTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>暂停 task（子循环挂起）。</summary>
    Task PauseTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>恢复 task（子循环继续）。</summary>
    Task ResumeTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>终止 task（子循环停止）。</summary>
    Task TerminateTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>收割子代理 node 产出（该工作流全部节点输出）。</summary>
    Task<IReadOnlyList<NodeOutput>> HarvestNodesAsync(string workflowId, CancellationToken ct = default);

    /// <summary>把 node 挂到 task（task→node 归属边，M:N）。</summary>
    Task AttachNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);
}

/// <summary>
/// 主循环编排器（默认实现，SingleImplementation / TH-12）：把编排动作落到纯逻辑端口。零 LLM 依赖。
/// 依赖可空（未装配 → no-op），与「可空守卫」模式一致。
/// </summary>
public sealed class TaskOrchestrator : ITaskOrchestrator
{
    // 成员任务幂等账（进程内）：同一 taskKey 只建一次；失败即回滚，避免"同轮重复建"。
    // E7：有界保留（上限 + 先进先出淘汰）由框架 BoundedKeyLedger 单点承载（避免进程级只增不清导致长跑内存增长）。
    private const int MemberTaskRetentionCap = 8192;
    private static readonly BoundedKeyLedger<byte> MemberTasks = new(MemberTaskRetentionCap);

    private static bool TryRegisterMember(string taskKey) => MemberTasks.TryRegister(taskKey, 0);

    private readonly INodeOutputStore? _nodes;
    private readonly ITaskLifecyclePort? _lifecycle;
    // task→node 归属边（唯一写入口 = IMountPort）：生产 = 服务槽装配的挂载门面；延迟取值以避开装配顺序。
    // 未装配 ⇒ FlowMissing 可见化（禁静默丢边）。
    private readonly Func<IMountPort?>? _mountProvider;
    // 任务命令唯一实现（C6）：建 task + 显式归拢父历史；null ⇒ 仅做生命周期簿记（迁移期旧宿主）。
    private readonly ITaskCommandPort? _commands;
    private readonly IDiagnosticsSink? _diagnostics;
    // 宿主钩子（端口化）：Turn 归属登记 / 种子判据；默认空实现保持通用层可独立运行。
    private readonly ITaskTurnObserver _turnObserver;
    private readonly ITaskSeedPolicy _seedPolicy;
    // 执行归属（A3）：驱动必须先取令牌；未装配 ⇒ 不受限实现（显式形态，仍产租约供观测）。
    private readonly global::Modules.Governance.Ownership.IExecutionOwnership _ownership;
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, string> _leases =
        new(StringComparer.Ordinal);

    /// <param name="nodes">节点输出端口（node 编排/收割）。</param>
    /// <param name="lifecycle">task 生命周期端口（task 状态跃迁 → 子循环启停）。</param>
    /// <param name="diagnostics">诊断端口（可空 = 不输出）。</param>
    /// <param name="mountProvider">挂载端口提供者（task→node 归属边唯一写入口；延迟取值，可空 = 未装配）。</param>
    /// <param name="turnObserver">Turn 归属登记钩子（宿主实现；缺省空实现）。</param>
    /// <param name="seedPolicy">种子判据钩子（宿主实现；缺省宽松）。</param>
    public TaskOrchestrator(
        INodeOutputStore? nodes = null,
        ITaskLifecyclePort? lifecycle = null,
        Func<IMountPort?>? mountProvider = null,
        IDiagnosticsSink? diagnostics = null,
        ITaskCommandPort? commands = null,
        global::Modules.Governance.Ownership.IExecutionOwnership? ownership = null,
        ITaskTurnObserver? turnObserver = null,
        ITaskSeedPolicy? seedPolicy = null)
    {
        _nodes = nodes;
        _lifecycle = lifecycle;
        _mountProvider = mountProvider;
        _diagnostics = diagnostics;
        _commands = commands;
        _ownership = ownership ?? global::Modules.Governance.Ownership.UnrestrictedExecutionOwnership.Instance;
        _turnObserver = turnObserver ?? NullTaskTurnObserver.Instance;
        _seedPolicy = seedPolicy ?? LenientTaskSeedPolicy.Instance;
    }

    /// <summary>建 task 并显式归拢父历史；无父 ⇒ 孤立 task。history 边只经命令服务写。</summary>
    public async Task CreateTaskAsync(string historyId, string taskKey, string? input = null, CancellationToken ct = default)
    {
        // S5：入口判据 —— 空种子不落库、不激活（此前迟至消费期 Ensure 才抛）。
        RequireNonEmptySeed(input);
        if (_commands is not null && TaskKey.TryCreate(taskKey, out var key))
        {
            await _commands.CreateAsync(BuildCreateTaskCommand(
                key, historyId, input, null, null, "task-orchestrator"), ct).ConfigureAwait(false);
            return;
        }

        if (_lifecycle is not null)
            await _lifecycle.CreateTaskAsync(taskKey, input, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 建成员 task（CreateMember 策略）：供给流挂 history → 生命周期写入（含标签/执行器）→ 激活消费。
    /// </summary>
    public async Task<bool> CreateMemberTaskAsync(
        string historyId, string taskKey, string? input,
        string? subAgentTag, string? executorId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        RequireNonEmptySeed(input);

        if (!TryRegisterMember(taskKey))
        {
            _diagnostics?.Report("TaskOrchestrator_member_reused", new { historyId, taskKey });
            return true;
        }

        try
        {
            if (_commands is not null)
            {
                var key = TaskKey.Create(taskKey);
                await _commands.CreateAsync(BuildCreateTaskCommand(
                    key, historyId, input, subAgentTag, executorId, "task-orchestrator-member"), ct)
                    .ConfigureAwait(false);
            }
            else if (_lifecycle is not null)
            {
                await _lifecycle.CreateTaskAsync(taskKey, input, subAgentTag, executorId, ct).ConfigureAwait(false);
            }

            // K-03：建成员只建不驱动（激活由调用方经 ActivateTaskAsync 显式声明）。
            // 宿主投递端口（TaskDispatchPortHost）已按 Activate:true 显式驱动，故此处内部激活 = 重复驱动。
            _diagnostics?.Report("TaskOrchestrator_member_created", new { historyId, taskKey, subAgentTag, executorId });
            // Turn 生命周期：成员任务归属唯一登记（后续 run/LLM/归档按 taskKey 回挂同一 Turn）。
            _turnObserver.AttachTask(historyId, taskKey);
            return true;
        }
        catch (Exception ex)
        {
            // 失败补偿：不留下“已登记但未激活”的幽灵成员；调用方据异常返回 DISPATCH_PROVISION_FAILED。
            MemberTasks.Remove(taskKey);
            _diagnostics?.Report("TaskOrchestrator_member_failed", new { historyId, taskKey, error = ex.Message });
            throw;
        }
    }

    /// <summary>S5 入口判据：可执行条目的种子必须非空（回退开关 = <c>SeedWriter.RejectEmptyOnEnqueue</c>）。</summary>
    private void RequireNonEmptySeed(string? input)
    {
        if (!_seedPolicy.RejectEmptyOnEnqueue) return;
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("seed.empty-input");
    }

    /// <summary>DUP-8：CreateTaskCommand 单一构造点（role/parent 规则只写一份）。</summary>
    private static CreateTaskCommand BuildCreateTaskCommand(
        TaskKey key, string? historyId, string? input,
        string? subAgentTag, string? executorId, string actor)
    {
        var parents = string.IsNullOrWhiteSpace(historyId)
            ? Array.Empty<ContainerKey>()
            : new[] { ContainerKey.Create(historyId) };
        return new CreateTaskCommand(
            key, parents, parents.Length > 0 ? SessionRole.TaskMember : SessionRole.StandaloneTask,
            subAgentTag, executorId, CreationIntent.SystemExplicit, actor, input);
    }

    /// <summary>
    /// 激活（唯一驱动入口）：先取执行归属令牌（A3）；已被其他驱动持有 ⇒ 结构化拒绝并可见化，
    /// 不覆盖、不排队（F3）。成功激活后登记租约号，终止时释放。
    /// </summary>
    public async Task ActivateTaskAsync(string taskKey, CancellationToken ct = default)
    {
        // 本驱动已持有 ⇒ 续用（顺序性重复驱动合法：如完成后新一轮）；他驱动持有才拒绝（F3）。
        if (_leases.ContainsKey(taskKey))
        {
            if (_lifecycle is null)
            {
                await LifecyclePortMissing(nameof(ActivateTaskAsync), taskKey).ConfigureAwait(false);
                return;
            }

            await _lifecycle.ActivateTaskAsync(taskKey, ct).ConfigureAwait(false);
            return;
        }

        var key = global::Modules.Governance.Ownership.OwnershipKey.Of("task", taskKey);
        var acquired = _ownership.TryAcquire(
            key,
            global::Modules.Governance.Ownership.DriverKind.TaskPool,
            nameof(TaskOrchestrator),
            TimeSpan.FromMinutes(10));

        if (!acquired.Acquired)
        {
            _diagnostics?.Report("TaskOrchestrator_drive_conflict", new
            {
                taskKey,
                code = global::Modules.KeySpace.Diagnostics.KeyReasonCodes.ExecDriverConflict,
                holder = acquired.Lease?.Driver.ToString(),
                leaseId = acquired.Lease?.LeaseId,
                diagnostic = acquired.Diagnostic,
            });
            return;
        }

        if (acquired.Lease is not null) _leases[taskKey] = acquired.Lease.LeaseId;
        if (_lifecycle is null)
        {
            await LifecyclePortMissing(nameof(ActivateTaskAsync), taskKey).ConfigureAwait(false);
            return;
        }

        await _lifecycle.ActivateTaskAsync(taskKey, ct).ConfigureAwait(false);
    }

    public Task PauseTaskAsync(string taskKey, CancellationToken ct = default)
        => _lifecycle?.PauseTaskAsync(taskKey, ct) ?? LifecyclePortMissing(nameof(PauseTaskAsync), taskKey);

    public Task ResumeTaskAsync(string taskKey, CancellationToken ct = default)
        => _lifecycle?.ResumeTaskAsync(taskKey, ct) ?? LifecyclePortMissing(nameof(ResumeTaskAsync), taskKey);

    public Task TerminateTaskAsync(string taskKey, CancellationToken ct = default)
    {
        ReleaseOwnership(taskKey);
        return _lifecycle?.TerminateTaskAsync(taskKey, ct) ?? LifecyclePortMissing(nameof(TerminateTaskAsync), taskKey);
    }

    /// <summary>释放执行归属（失败/终止/成功终态统一出口）：仅释放本驱动持有的租约。</summary>
    public void ReleaseOwnership(string taskKey)
    {
        if (!_leases.TryRemove(taskKey, out var leaseId)) return;
        _ownership.Release(global::Modules.Governance.Ownership.OwnershipKey.Of("task", taskKey), leaseId);
    }

    /// <summary>生命周期端口未装配：no-op 但可见化（E8：禁调用方把 no-op 误判为「已执行成功」）。</summary>
    private Task LifecyclePortMissing(string op, string taskKey)
    {
        _diagnostics?.Report("TaskOrchestrator_LifecyclePort_missing", new { op, taskKey });
        return Task.CompletedTask;
    }

    /// <summary>收割该工作流全部节点输出；节点端口未装配返回空集。</summary>
    public async Task<IReadOnlyList<NodeOutput>> HarvestNodesAsync(string workflowId, CancellationToken ct = default)
    {
        if (_nodes is null) return Array.Empty<NodeOutput>();
        var outputs = await _nodes.EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false);
        return outputs ?? Array.Empty<NodeOutput>();
    }

    /// <summary>把 node 挂到 task（task→node 归属边，M:N）；经唯一写入口 IMountPort。未装配 ⇒ 可见化。</summary>
    public async Task AttachNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        var mounts = _mountProvider?.Invoke();
        if (mounts is null)
        {
            _diagnostics?.Report(global::Operation.HierarchyMethod.Abstractions.DiagnosticsCodes.FlowMissing, new
            {
                context = "TaskOrchestrator_attribution",
                taskKey,
                workflowId,
                nodeKey,
            });
            return;
        }

        var receipt = await mounts.ApplyAsync(new MountRequest(
            MountIds.NodeToTask,
            new MountScope("task", taskKey, Namespace: workflowId, CorrelationId: taskKey),
            [new MountTarget("node", nodeKey)],
            MountIntent.Ensure,
            MountOrigin.Live,
            MountTriggers.NodePersisted,
            Actor: "task-orchestrator"), ct).ConfigureAwait(false);

        if (!receipt.Ok)
        {
            _diagnostics?.Report(global::Operation.HierarchyMethod.Abstractions.DiagnosticsCodes.FlowMissing, new
            {
                context = "TaskOrchestrator_attribution",
                taskKey,
                workflowId,
                nodeKey,
                reason = receipt.Reason ?? receipt.Outcome.ToString(),
            });
        }
    }

    /// <summary>
    /// 声明式编排入口（参数化公开面）：<paramref name="dependencyPortsJson"/> 声明端口面开关
    /// （<c>ports.references</c> / <c>ports.outputs</c> / <c>ports.lifecycle</c>，缺省全开）、节点产出种子
    /// （<c>seed[{workflowId,nodeKey,port,value}]</c>）与动作脚本（<c>operations[{op,…}]</c>，op ∈ createTask /
    /// activateTask / pauseTask / resumeTask / terminateTask / attachNode / harvestNodes），按脚本顺序编排后
    /// 产出 <see cref="TaskOrchestratorRunSnapshot"/>（端口未装配 → no-op / 空集口径不变）。
    /// <para>同步返回（端口绑定面为值型，不作 await）。</para>
    /// </summary>
    public static TaskOrchestratorRunSnapshot RunTask(string dependencyPortsJson)
    {
        var declared = DeclaredOrchestration.Parse(dependencyPortsJson);

        var references = declared.Ports.References ? new MemoryHierarchyReferenceManager() : null;
        var nodes = declared.Ports.Outputs ? new MemoryNodeOutputStore() : null;
        var lifecycle = declared.Ports.Lifecycle ? new DeclaredTaskLifecyclePort() : null;

        foreach (var seed in declared.Seeds)
            nodes?.Seed(seed.WorkflowId, seed.NodeKey, seed.Port, seed.Value);

        // 测试面端口（B9 收口）：内存挂载桩承载写边语义（直接改内存，不引生产写边方法）。
        var mountPort = references is null ? null : new MemoryMountPort(references);
        var orchestrator = new TaskOrchestrator(
            nodes, lifecycle,
            mountProvider: mountPort is null ? null : () => mountPort);

        // 编排 = 配方（声明面 → 框架唯一编译入口 → 拓扑驱动）：动作分派由 CircuitNodeResolver 单源解析
        // —— 手写 `switch (operation.Op)` 与 `default: throw` 已删；未注册 op 由框架「未注册的电路节点 Kind」文案报出。
        var context = new OrchestrationRecipeContext(orchestrator, references, lifecycle);
        var resolver = RegisterActions(context);
        TaskOrchestratorRunSnapshot? snapshot = null;

        CircuitAssemblyRunner.RunAsync(
                declared.Recipe,
                EmptyEntry,
                resolver.Resolve,
                CancellationToken.None,
                new[] { new ExitBinding(SnapshotExitPort, value => snapshot = value as TaskOrchestratorRunSnapshot) })
            .GetAwaiter().GetResult();

        return snapshot ?? throw new InvalidOperationException(
            "声明式编排入口未回投运行快照（配方出口 '" + SnapshotExitPort
            + "' 未产出快照步骤的结果）：编排配方与出口绑定不一致。");
    }

    /// <summary>快照步骤的节点 Id（非 op：不入词表，只承载七字段回投）。</summary>
    private const string SnapshotNodeId = "__snapshot";

    /// <summary>快照载荷端口名。</summary>
    private const string SnapshotPort = "snapshot";

    /// <summary>快照出口名（= 快照步骤的 "NodeId.Port"；配方收集与出口绑定同源）。</summary>
    private const string SnapshotExitPort = SnapshotNodeId + "." + SnapshotPort;

    /// <summary>快照步骤的节点 Kind（与 op 词表无交集）。</summary>
    private const string SnapshotActionKind = "taskOrchestratorSnapshot";

    /// <summary>每步**统一**声明的动作槽位（按 op 取用；值经 <c>Literal</c> 逐步骤注入 ⇒ 同 Kind 多步骤各带各的参）。</summary>
    private static readonly string[] ActionPorts = { "historyId", "taskKey", "input", "workflowId", "nodeKey" };

    private static readonly IReadOnlyDictionary<string, object?> EmptyEntry =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> EmptyOutputs =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>动作体签名（上下文 + 具名入端口 → 具名出端口；出端口为空集 = 无产物）。</summary>
    private delegate Task<IReadOnlyDictionary<string, object?>> ActionBody(
        OrchestrationRecipeContext context,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken);

    /// <summary>
    /// 编排动作词表（D8 / F11 **单源**）：LLM/函数短动词 → 声明面 canonical op。
    /// 未映射且未声明的动作 ⇒ null（调用方拒绝，禁第二套词表各自放行）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ActionAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = "createTask",
            ["activate"] = "activateTask",
            ["pause"] = "pauseTask",
            ["resume"] = "resumeTask",
            ["terminate"] = "terminateTask",
            ["harvest"] = "harvestNodes",
            ["attach"] = "attachNode",
        };

    /// <summary>外部动作词 → 声明面 canonical op，并以 <c>orchestration-ops.ushar</c> 词表裁决（未声明 ⇒ null）。</summary>
    public static string? ResolveDeclaredOp(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var name = action.Trim();
        var op = ActionAliases.TryGetValue(name, out var canonical) ? canonical : name;
        return OrchestrationOpsVocabulary.IsDeclaredOp(op) ? op : null;
    }

    /// <summary>
    /// op → 动作体（**动作分派的唯一表**：Kind = op 名，经 <see cref="CircuitNodeResolver"/> 解析）。
    /// 未注册 op 的文案由框架单源给出 —— 本表无 `default: throw`（漂移兜底分支已删）。
    /// </summary>
    private static readonly Dictionary<string, ActionBody> ActionBodies = new(StringComparer.Ordinal)
    {
        ["createTask"] = async (c, i, ct) =>
        {
            var historyId = Arg(i, "historyId");
            var taskKey = Arg(i, "taskKey");
            c.CreateTaskLinks.Add(new CreateTaskLink(historyId, taskKey));
            await c.Orchestrator.CreateTaskAsync(historyId!, taskKey!, Arg(i, "input"), ct).ConfigureAwait(false);
            return EmptyOutputs;
        },
        ["activateTask"] = (c, i, ct) => Wrap(c.Orchestrator.ActivateTaskAsync(Arg(i, "taskKey")!, ct)),
        ["pauseTask"] = (c, i, ct) => Wrap(c.Orchestrator.PauseTaskAsync(Arg(i, "taskKey")!, ct)),
        ["resumeTask"] = (c, i, ct) => Wrap(c.Orchestrator.ResumeTaskAsync(Arg(i, "taskKey")!, ct)),
        ["terminateTask"] = (c, i, ct) => Wrap(c.Orchestrator.TerminateTaskAsync(Arg(i, "taskKey")!, ct)),
        ["attachNode"] = (c, i, ct) =>
        {
            var taskKey = Arg(i, "taskKey");
            var nodeKey = Arg(i, "nodeKey");
            c.AttachLinks.Add(new AttachLink(taskKey, nodeKey));
            return Wrap(c.Orchestrator.AttachNodeAsync(taskKey!, Arg(i, "workflowId")!, nodeKey!, ct));
        },
        ["harvestNodes"] = async (c, i, ct) =>
        {
            c.Harvested = await c.Orchestrator.HarvestNodesAsync(Arg(i, "workflowId")!, ct).ConfigureAwait(false);
            return EmptyOutputs;
        },
    };

    /// <summary>无产物动作适配（等待副作用完成 → 空出端口）。</summary>
    private static async Task<IReadOnlyDictionary<string, object?>> Wrap(Task action)
    {
        await action.ConfigureAwait(false);
        return EmptyOutputs;
    }

    /// <summary>动作表 → 节点解析表（Kind = op 名；同 Kind 多步骤复用同一节点体，因参数经端口而非闭包）。</summary>
    private static CircuitNodeResolver RegisterActions(OrchestrationRecipeContext context)
    {
        var resolver = new CircuitNodeResolver();
        foreach (var pair in ActionBodies)
            resolver.Register(pair.Key, new DelegateCircuitNode(pair.Key, (inputs, ct) => pair.Value(context, inputs, ct)));

        resolver.Register(SnapshotActionKind,
            new DelegateCircuitNode(SnapshotActionKind, (_, _) => Task.FromResult(Capture(context))));
        return resolver;
    }

    /// <summary>具名入端口取值（动作槽位为字符串；非字符串 / 缺失 ⇒ null，与原声明面口径一致）。</summary>
    private static string? Arg(IReadOnlyDictionary<string, object?> inputs, string name)
        => inputs.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>声明式编排（入口私有形态）：端口面开关 + 节点产出种子 + **编排配方**（声明面与执行序的单源）。</summary>
    private sealed record DeclaredOrchestration(
        PortSwitches Ports,
        IReadOnlyList<OutputSeed> Seeds,
        CircuitRecipeSpec Recipe)
    {
        /// <summary>声明文本 → 编排声明（根非对象 / 缺必填槽位即抛，不静默降级）。</summary>
        internal static DeclaredOrchestration Parse(string dependencyPortsJson)
        {
            if (string.IsNullOrWhiteSpace(dependencyPortsJson))
                throw new InvalidOperationException("声明式编排入口需要依赖端口声明 JSON（dependencyPortsJson 不得为空）");

            JsonElement root;
            using (var document = JsonDocument.Parse(dependencyPortsJson))
                root = document.RootElement.Clone();

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("声明式编排入口的依赖端口声明须为 JSON 对象");

            return new DeclaredOrchestration(ParseSwitches(root), ParseSeeds(root), BuildRecipe(root));
        }

        /// <summary>端口面开关（<c>ports</c> 缺失 = 三端口全装配）。</summary>
        private static PortSwitches ParseSwitches(JsonElement root)
        {
            if (!root.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Object)
                return new PortSwitches(true, true, true);

            return new PortSwitches(
                References: GetBool(ports, "references", fallback: true),
                Outputs: GetBool(ports, "outputs", fallback: true),
                Lifecycle: GetBool(ports, "lifecycle", fallback: true));
        }

        private static IReadOnlyList<OutputSeed> ParseSeeds(JsonElement root)
        {
            var seeds = new List<OutputSeed>();
            if (!root.TryGetProperty("seed", out var array) || array.ValueKind != JsonValueKind.Array)
                return seeds;

            foreach (var seed in array.EnumerateArray())
            {
                seeds.Add(new OutputSeed(
                    Required(seed, "workflowId"), Required(seed, "nodeKey"), Required(seed, "port"),
                    Scalar(seed, "value")));
            }

            return seeds;
        }

        /// <summary>
        /// 动作脚本 → 编排配方：每 op 一步（节点 Id = <c>op#i</c> 唯一，StepKind = op 名 ⇒ 同 Kind 可多步），
        /// 步序即声明序（顺序链相邻成边 = 原 <c>foreach</c> 语义）；参数经 <c>Literal</c> 逐步骤注入；
        /// 末步 = 快照步骤（出口回投载体）。
        /// <para>词表单源（链 5）：op 必须 ∈ 声明面 <c>Config/orchestration-ops.ushar</c>；未声明即拒（先于任何副作用）。</para>
        /// </summary>
        private static CircuitRecipeSpec BuildRecipe(JsonElement root)
        {
            var recipe = new RecipeBuilder(nameof(TaskOrchestrator), Array.Empty<string>());
            if (root.TryGetProperty("operations", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var step in array.EnumerateArray())
                {
                    var op = Required(step, "op");
                    if (!OrchestrationOpsVocabulary.IsDeclaredOp(op))
                        throw new InvalidOperationException(
                            $"声明式编排入口收到未声明动作 '{op}'"
                            + $"（词表真源：Config/{OrchestrationOpsVocabulary.DeclarationFileName}"
                            + $" 的 [Space] {OrchestrationOpsVocabulary.SpaceId}）");

                    var nodeId = $"{op}#{index++}";
                    recipe.Step(nodeId, op, inPorts: ActionPorts);
                    recipe.Literal(nodeId, "historyId", GetString(step, "historyId"));
                    recipe.Literal(nodeId, "taskKey", GetString(step, "taskKey"));
                    recipe.Literal(nodeId, "input", GetString(step, "input"));
                    recipe.Literal(nodeId, "workflowId", GetString(step, "workflowId"));
                    recipe.Literal(nodeId, "nodeKey", GetString(step, "nodeKey"));
                }
            }

            // 快照步骤（末步，非 op）：产出七字段供出口绑定回投（须显式声明输出端口，否则 Collect 判「端口未声明」）。
            recipe.Step(SnapshotNodeId, SnapshotActionKind, outPorts: new[] { SnapshotPort });
            recipe.Collect(SnapshotNodeId, SnapshotPort);
            return recipe.Build();
        }

        /// <summary>必填字符串槽位（缺失 / 空白即抛）。</summary>
        private static string Required(JsonElement element, string name)
            => GetString(element, name) is { } value && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"声明式编排入口的声明缺少 '{name}'");

        private static string? GetString(JsonElement element, string name)
            => Share.JsonPayload.PayloadAccessor.Str(element, name);

        private static bool GetBool(JsonElement element, string name, bool fallback)
            => Share.JsonPayload.PayloadAccessor.Bool(element, name) ?? fallback;

        /// <summary>JSON 标量 → CLR 值（字符串 / 整数 / 浮点 / 布尔；其余 → null）。</summary>
        private static object? Scalar(JsonElement element, string name)
            => element.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.TryGetInt64(out var number) ? number : value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                }
                : null;
    }

    /// <summary>端口面开关（false = 该端口不装配 → 编排动作按 no-op / 空集口径）。</summary>
    private sealed record PortSwitches(bool References, bool Outputs, bool Lifecycle);

    /// <summary>节点产出种子（声明式铺数据，不经契约方法）。</summary>
    private sealed record OutputSeed(string WorkflowId, string NodeKey, string Port, object? Value);

    /// <summary>声明面 createTask 请求（快照 historyTaskLinked 判据的输入，对齐原「按声明项对账」口径）。</summary>
    private sealed record CreateTaskLink(string? HistoryId, string? TaskKey);

    /// <summary>声明面 attachNode 请求（快照 taskNodeLinked 判据的输入）。</summary>
    private sealed record AttachLink(string? TaskKey, string? NodeKey);

    /// <summary>配方执行上下文：端口实例 + 快照判据所需的声明面事实（副作用经端口，不入配方）。</summary>
    /// <summary>
    /// 内存挂载桩（声明式编排运行面）：把 <c>node_to_task</c> 写边落到 <see cref="MemoryHierarchyReferenceManager"/>，
    /// 供快照判据复用同一事实面；其余挂载不参与（无快照消费方，显式 Committed 不伪造边）。
    /// </summary>
    private sealed class MemoryMountPort : IMountPort
    {
        private readonly MemoryHierarchyReferenceManager _references;

        public MemoryMountPort(MemoryHierarchyReferenceManager references) => _references = references;

        public async Task<MountReceipt> ApplyAsync(MountRequest request, CancellationToken ct = default)
        {
            var targets = request.Targets ?? Array.Empty<MountTarget>();

            if (string.Equals(request.MountId, MountIds.NodeToTask, StringComparison.Ordinal))
            {
                var workflowId = request.Scope.Namespace ?? string.Empty;
                foreach (var target in targets)
                {
                    if (request.Intent == MountIntent.Remove)
                    {
                        await _references.UnlinkMemberNodeAsync(
                            request.Scope.ContainerKey, workflowId, target.Key, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await _references.LinkMemberNodeAsync(
                            request.Scope.ContainerKey, workflowId, target.Key, ct).ConfigureAwait(false);
                    }
                }
            }

            return new MountReceipt(
                request.MountId, "mount_node_task", "cross_db_link",
                request.Origin, request.Trigger, request.CorrelationId,
                targets.Select(t => new MountTargetResult(
                    t.Entity, t.Key, MountTargetStatus.Applied, "cross_db_link")).ToList(),
                targets.Count, 0, 0, true, null, MountOutcome.Committed);
        }
    }

    private sealed class OrchestrationRecipeContext
    {
        public OrchestrationRecipeContext(
            TaskOrchestrator orchestrator,
            MemoryHierarchyReferenceManager? references,
            DeclaredTaskLifecyclePort? lifecycle)
        {
            Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            References = references;
            Lifecycle = lifecycle;
        }

        public TaskOrchestrator Orchestrator { get; }

        public MemoryHierarchyReferenceManager? References { get; }

        public DeclaredTaskLifecyclePort? Lifecycle { get; }

        public List<CreateTaskLink> CreateTaskLinks { get; } = new();

        public List<AttachLink> AttachLinks { get; } = new();

        /// <summary>最近一次 harvestNodes 产出（与原先「循环内覆盖式赋值」同语义）。</summary>
        public IReadOnlyList<NodeOutput> Harvested { get; set; } = Array.Empty<NodeOutput>();
    }

    /// <summary>快照步骤：七字段一次产出（出口绑定回投的载荷）。</summary>
    private static IReadOnlyDictionary<string, object?> Capture(OrchestrationRecipeContext context)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [SnapshotPort] = new TaskOrchestratorRunSnapshot(
                HistoriesCount: context.References?.Histories.Count ?? 0,
                HistoryTaskLinked: context.References is not null
                    && context.CreateTaskLinks.Any(l => context.References!.HasHistoryTask(l.HistoryId!, l.TaskKey!)),
                CreatedPair: context.Lifecycle?.Created is { } created
                    ? $"{created.Key}|{created.Input}"
                    : string.Empty,
                TaskNodeLinked: context.References is not null
                    && context.AttachLinks.Any(l => context.References!.HasTaskNode(l.TaskKey!, l.NodeKey!)),
                OutputCount: context.Harvested.Count,
                FirstNodeKey: context.Harvested.Count > 0 ? context.Harvested[0].NodeKey : string.Empty,
                LifecycleCalls: context.Lifecycle?.Calls.ToArray() ?? Array.Empty<string>()),
        };
}

/// <summary>
/// 编排运行快照（<see cref="TaskOrchestrator.RunTaskAsync"/> 的产物形状）：7 只读字段即宿主对
/// 「建账 / 挂节点 / 收割 / 生命周期委派」的观测面（字段名与声明面逐字对齐，改名即契约漂移）。
/// </summary>
public sealed record TaskOrchestratorRunSnapshot(
    int HistoriesCount,
    bool HistoryTaskLinked,
    string CreatedPair,
    bool TaskNodeLinked,
    int OutputCount,
    string FirstNodeKey,
    string[] LifecycleCalls);
