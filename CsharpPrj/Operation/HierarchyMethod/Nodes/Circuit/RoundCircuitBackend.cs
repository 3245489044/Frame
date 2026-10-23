using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Share.Foundations.Diagnostics;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Circuit;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Execution;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 轮电路执行后端（IRoundExecutionBackend 的 Circuit 实现）：
    /// seed → 配方构建（编译期产物图校验）→ 投影/编译（环/拓扑）→ CircuitEngine 拓扑驱动执行
    /// （每步经 RoundStepCircuitNode 保留原步骤语义 + onAfterStep 横切 + 错误强类型归因）
    /// → 决策读取 → 成功准则裁决 → harvest → NodeRoundResult。
    /// 语义对齐 NodeGraphOrchestrator：取消原样上抛；步骤错误归因后继续裁决/harvest；跳过步骤不入记录。
    /// 门控：FailFast（默认）= 编译违归即抛（电路为唯一执行后端，同时开启 InputPorts 契约强校验）；
    /// LogOnly = 编译违归记日志并回退既有编排器（迁移期）。
    /// </summary>
    public sealed class RoundCircuitBackend : IRoundExecutionBackend
    {
        private readonly INodeExecutor _executor;
        private readonly INodeExecutorHooks? _hooks;
        private readonly IWorkflowDecisionPolicy? _decisions;
        private readonly bool _failFast;
        private readonly NodeGraphOrchestrator? _fallback;
        // 诊断可见化端口（缺省空实现 = 不记）：分层无环——框架只依赖 IDiagnosticsSink，宿主日志门面经组合根绑定。
        private readonly IDiagnosticsSink? _diagnostics;

        public RoundCircuitBackend(
            INodeExecutor executor,
            INodeExecutorHooks? hooks = null,
            IWorkflowDecisionPolicy? decisions = null,
            IRoundStatePort? statePort = null,
            bool failFast = true,
            NodeGraphOrchestrator? fallback = null,
            IDiagnosticsSink? diagnostics = null)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _hooks = hooks;
            _decisions = decisions;
            // statePort 保留形参仅为装配兼容：seed/harvest 已上移 RoundContractBackend（单一裁决点）。
            _ = statePort;
            _failFast = failFast;
            _fallback = fallback;
            _diagnostics = diagnostics;
        }

        public async Task<NodeRoundResult> RunRoundAsync(
            RoundSpec spec, NodeRoundContext ctx, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(ctx);

            // 编译阶段（配方构建 + 产物图校验 + 投影 + 编译）：违归在 LogOnly 下回退既有编排器
            // （seed/harvest 已上移 RoundContractBackend，fallback 只执行）。
            RoundFlowCircuitRegistry.BuiltCircuit built;
            try
            {
                built = RoundFlowCircuitRegistry.GetOrBuild(spec, enforceDeclaredInputs: _failFast);
            }
            catch (Exception ex) when (!_failFast && _fallback is not null)
            {
                _diagnostics?.Report("RoundCircuitBackend_compile_fallback",
                    new { workflow = spec.Name, error = ex.Message });
                return await _fallback.RunRoundAsync(spec, ctx, ct).ConfigureAwait(false);
            }

            var records = new List<NodeExecutionRecord>();
            var skippedSteps = new List<string>();
            try
            {
                var stepsById = spec.Steps.ToDictionary(s => s.EffectiveNodeId, StringComparer.Ordinal);
                var resolver = new RoundCircuitNodeResolver(stepsById, _executor, _hooks, ctx, spec.GlobalKeyMapping);
                await CircuitEngine.ExecuteAsync(built.Circuit, built.Plan, resolver.Resolve, null, ct)
                    .ConfigureAwait(false);
                // 对齐编排器取消语义：取消不在电路内部判错（跳过/短路），返回后仍强归一为 OCE 上抛。
                ct.ThrowIfCancellationRequested();
                // D16：收集执行器原始记录（不重建），记录逐字段保真。
                records = ctx.RecordsInDeclarationOrder(spec).ToList();
                // 执行器返回 null = CanExecute 合法跳过（禁与「缺步」混同）；E1 判据据此不误报。
                var executedKeys = new HashSet<string>(records.Select(static r => r.NodeKey), StringComparer.Ordinal);
                skippedSteps = spec.Steps
                    .Select(static s => s.EffectiveNodeId)
                    .Where(id => !executedKeys.Contains(id))
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw; // 取消交由调用方按取消语义处理（对齐编排器）
            }
            catch (Exception ex)
            {
                // 步骤错误已由节点体强类型归因（ctx.Error 非空）→ 与编排器一致：归集后继续裁决/harvest；
                // 基础设施错误（如 Exit 投影）→ 编排器同款兜底归因（Error 取消息、ErrorStep 取最后成功步骤）。
                if (ex is RoundFailureException failure)
                {
                    var f = failure.Failure.CompletedSteps.Count > 0
                        ? failure.Failure
                        : failure.Failure with { CompletedSteps = ctx.ExecutedSteps.ToArray() };
                    ctx.Failure ??= f;
                    ctx.FailedStep ??= f.FailedStep;
                }
                if (ctx.Error is null)
                {
                    ctx.Error = ex.Message;
                    var last = records.Count > 0 ? records[^1] : NodeExecutionRecordFactory.Empty;
                    if (_hooks is not null)
                        await _hooks.OnStepErrorAsync(ctx, last, ex, ct).ConfigureAwait(false);
                }
                records = ctx.RecordsInDeclarationOrder(spec).ToList();
            }

            var decision = ReadDecision(ctx);
            // 成功判定：与编排器同款（RunToEnd 无异常即成功；DecisionWord 有决策词且策略映射 Success/Continue 即成功，
            // 决策策略未装配时按契约回退「非空决策词视为继续」）。
            var ok = spec.SuccessCriterion switch
            {
                RoundSpecSuccessCriterion.RunToEnd => ctx.Error == null,
                _ => ctx.Error == null && decision != null &&
                     (_decisions?.Map(decision) ?? LoopDecision.Continue) is LoopDecision.Success or LoopDecision.Continue,
            };

            // 终答文本由轮驱动（WorkflowRunner / IFinalAnswerPolicy）单点归集，本后端不重复计算（消除每轮二次求解）。
            // 契约判据与 seed/harvest 由 RoundContractBackend 装饰器统一裁决（本后端只负责执行）。
            return new NodeRoundResult(records, decision, ok, ctx.Error, skippedSteps);
        }

        private sealed class RoundCircuitNodeResolver
        {
            private readonly IReadOnlyDictionary<string, RoundStep> _steps;
            private readonly INodeExecutor _executor;
            private readonly INodeExecutorHooks? _hooks;
            private readonly NodeRoundContext _ctx;
            private readonly KeyMappingConfig? _globalMapping;

            public RoundCircuitNodeResolver(
                IReadOnlyDictionary<string, RoundStep> steps, INodeExecutor executor,
                INodeExecutorHooks? hooks, NodeRoundContext ctx, KeyMappingConfig? globalMapping)
            {
                _steps = steps;
                _executor = executor;
                _hooks = hooks;
                _ctx = ctx;
                _globalMapping = globalMapping;
            }

            public ICircuitNode Resolve(string kind)
            {
                if (!_steps.TryGetValue(kind, out var step))
                    throw new InvalidOperationException(
                        $"[RoundFlow] 电路节点实现键 '{kind}' 未映射到 RoundStep（投影与执行不一致）。");
                return new RoundStepCircuitNode(step, _executor, _hooks, _ctx, _globalMapping);
            }
        }

        private static string? ReadDecision(NodeRoundContext ctx)
            => ctx.Vars.TryGet<object?>(NodeExecutionKeys.Decision, out var d) ? d?.ToString() : null;
    }
}
