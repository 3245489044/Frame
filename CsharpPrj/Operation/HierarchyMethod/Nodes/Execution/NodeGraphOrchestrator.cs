using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Center;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.HierarchySupport;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点图编排器（Nodes 层编排收敛，零 LLM）：按工作流（<see cref="RoundSpec"/> 节点集）顺序列表，
/// 委托注入的 <see cref="INodeExecutor"/> 执行节点 → 横切 <see cref="INodeExecutorHooks"/>（onAfterStep / onStepError）
/// → 依模板成功准则裁决 ok → 收割 <see cref="IRoundStatePort"/> → 终答归集 <see cref="IFinalAnswerPolicy"/>。
/// 自 Agent WorkflowRunner 主循环平移的中立骨架；Agent 专属（seed/harvest 键、guard_output、终态信封、return spec）
/// 经注入策略/执行器保留在 Agent 应用层。
/// </summary>
public sealed class NodeGraphOrchestrator
{
    private readonly INodeExecutor _executor;
    private readonly INodeExecutorHooks? _hooks;
    private readonly IWorkflowDecisionPolicy? _decisions;
    private readonly IFinalAnswerPolicy? _finalAnswer;
    private readonly IRoundStatePort? _statePort;

    /// <param name="executor">节点执行器（必需；Agent 侧 NodeExecutionService/RoundStepInvoker 适配器）。</param>
    /// <param name="hooks">横切钩子（onAfterStep / onStepError；可空，未装配 no-op）。</param>
    /// <param name="decisions">决策词判定策略（可空）。</param>
    /// <param name="finalAnswer">终答归集策略（可空）。</param>
    /// <param name="statePort">轮状态端口（seed/harvest；可空，纯内存路径 no-op）。</param>
    /// <param name="requireAllPorts">装配期端口契约严格校验（R10 fail-fast）：缺任一钩子/决策/终答/状态端口即抛错，不再静默 no-op。
    /// 默认 false 保留既有可空装配行为（直跑/单测零回归）；完整装配的组合根（Agent 生产路径已全装配）可开启以消除同链分叉。</param>
    public NodeGraphOrchestrator(
        INodeExecutor executor,
        INodeExecutorHooks? hooks = null,
        IWorkflowDecisionPolicy? decisions = null,
        IFinalAnswerPolicy? finalAnswer = null,
        IRoundStatePort? statePort = null,
        bool requireAllPorts = false)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _hooks = hooks;
        _decisions = decisions;
        _finalAnswer = finalAnswer;
        _statePort = statePort;
        if (requireAllPorts)
            PortContractGuard.Ensure(new[]
            {
                PortContractGuard.Port(nameof(hooks), _hooks),
                PortContractGuard.Port(nameof(decisions), _decisions),
                PortContractGuard.Port(nameof(finalAnswer), _finalAnswer),
                PortContractGuard.Port(nameof(statePort), _statePort),
            });
    }

    /// <summary>
    /// 执行一轮节点编排：seed → 顺序执行主路径步骤（execute + onAfterStep 横切，各自纳入 Center 可取消执行边界）→ 取消/错误归集 →
    /// 成功准则裁决 → harvest（收割 + 终态信封）→ 终答归集，返回 <see cref="NodeRoundResult"/>。
    /// <para>
    /// 编排层自身**不持有步骤/轮级预算**：轮级有界由上层轮驱动（Agent 侧 RoundLoopDriver / RoundTimeout）承载，步骤级有界由执行器
    /// （RoundStepInvoker 对确定性读源的上限窗口）承载；此处只把每步/持久化纳入 Center 边界的取消级联与强类型异常归一。
    /// 若在框架层隐式把整轮预算套到每一步（含 LLM/暂停步）会误杀真实长轮——LLM 步预算必须由执行器侧统一门承载，故不在此重载。
    /// </summary>
    public async Task<NodeRoundResult> RunRoundAsync(
        RoundSpec spec, NodeRoundContext ctx, CancellationToken ct = default)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        // Center 可取消执行边界根：以外部取消(ct)为基底、无预算（未给预算 = 直跑仅响应取消，零误杀）。
        using var scope = ExecutionScope.CreateRoot("round", external: ct);

        if (_statePort != null)
            await _statePort.SeedAsync(spec, ctx, ct).ConfigureAwait(false);

        var records = new List<NodeExecutionRecord>();
        NodeExecutionRecord? last = null;
        try
        {
            foreach (var step in spec.Steps)
            {
                scope.Token.ThrowIfCancellationRequested();
                // 委托节点执行器（含入参解析/函数调用/RecordStep 及 Agent 专属步骤横切）——纳入 Center 步骤级边界（取消级联 + 异常归一）。
                var record = await scope.RunStepAsync(
                    step.FuncName,
                    t => _executor.ExecuteStepAsync(step, ctx, spec.GlobalKeyMapping, t)).ConfigureAwait(false);
                if (record is null) continue; // 步骤未执行（如条件门控 CanExecute 跳过）
                last = record;
                records.Add(record);
                // 横切持久化（onAfterStep）：Agent 侧落 ur_nodes + SQL + 图关系；失败单独归类、不翻转业务 ok。
                // 持久化在编排热路径属存储侧访问 → 纳入 Center 存储侧边界（取消级联 + 异常归一）。
                if (_hooks != null)
                    await scope.RunStoreAsync(
                        $"persist:{record.NodeKey}",
                        t => _hooks.OnAfterStepAsync(ctx, record, t)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // 取消交由调用方按取消语义处理，不归为普通错误（对齐 WorkflowRunner 取消语义）
        }
        catch (Exception ex)
        {
            // 错误强类型归因（onStepError，对应 StaticAsyncPipeline.onStepError）：错误在编排数据空间强类型归集。
            if (ctx.Error is null) ctx.Error = ex.Message;
            if (_hooks != null)
                await _hooks.OnStepErrorAsync(ctx, last ?? RecordsFallback, ex, ct).ConfigureAwait(false);
        }

        var decision = ReadDecision(ctx);
        // 成功判定由模板声明的成功准则裁决：RunToEnd 无异常即成功；DecisionWord 有决策词且策略映射为
        // Success/Continue 即成功，决策策略未装配（_decisions==null）时按契约回退「非空决策词视为继续」（不静默判失败）。
        var ok = spec.SuccessCriterion switch
        {
            RoundSpecSuccessCriterion.RunToEnd => ctx.Error == null,
            _ => ctx.Error == null && decision != null &&
                 (_decisions?.Map(decision) ?? LoopDecision.Continue) is LoopDecision.Success or LoopDecision.Continue,
        };

        if (_statePort != null)
            await _statePort.HarvestAsync(spec, ctx, ct).ConfigureAwait(false);

        var vars = ctx.Vars.Snapshot();
        var text = _finalAnswer?.ResolveFinalAnswer(spec, records, vars) ?? "";

        return new NodeRoundResult(records, decision, ok, ctx.Error);
    }

    private static string? ReadDecision(NodeRoundContext ctx)
        => ctx.Vars.TryGet<object?>(NodeExecutionKeys.Decision, out var d) ? d?.ToString() : null;

    private static readonly NodeExecutionRecord RecordsFallback = new(
        string.Empty, string.Empty,
        new Dictionary<string, object?>(StringComparer.Ordinal),
        new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal));
}
