using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Judgment;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮生命周期唯一装饰器：<c>Seed → 种子闭合复核 → Execute → Judge → Terminal → Harvest</c>。
/// <para>不变量：① Seed/Harvest 只在此调用；② Harvest 在终态已定之后调用一次（失败轮也收割，保留现场，但不得改写终态）；
/// ③ 终答推导只在 Succeeded 时由上层调用；④ 两条后端只提供 Records + SkippedSteps + Decision，判据一律在本类。</para>
/// </summary>
public sealed class RoundLifecycleBackend : IRoundExecutionBackend
{
    private readonly IRoundExecutionBackend _inner;
    private readonly IRoundStatePort? _statePort;
    private readonly RoundTerminalPolicyOptions _terminalOptions;
    private readonly Func<string?, Share.TaskStatusPool.Loops.LoopDecision>? _decisionMapper;
    private readonly bool _enabled;

    public RoundLifecycleBackend(
        IRoundExecutionBackend inner,
        IRoundStatePort? statePort = null,
        RoundTerminalPolicyOptions? terminalOptions = null,
        Func<string?, Share.TaskStatusPool.Loops.LoopDecision>? decisionMapper = null,
        bool enabled = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _statePort = statePort;
        _terminalOptions = terminalOptions ?? RoundTerminalPolicyOptions.Default;
        _decisionMapper = decisionMapper;
        _enabled = enabled;
    }

    public async Task<NodeRoundResult> RunRoundAsync(
        RoundSpec spec, NodeRoundContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!_enabled)
            return await _inner.RunRoundAsync(spec, ctx, ct).ConfigureAwait(false);

        if (_statePort is not null)
            await _statePort.SeedAsync(spec, ctx, ct).ConfigureAwait(false);

        // 种子闭合：**不在运行期硬失败**。一次性声明流（context-projection / result-writeback）的入参
        // 由调用方经 state/收集端口供给，不在轮种子集内 —— 运行期硬判会把合法流误杀为 seed.port-uncovered。
        // 生成期判据见 PortClosureGuard.Verify（仅对显式声明 InputPorts 的流生效）。

        var result = await _inner.RunRoundAsync(spec, ctx, ct).ConfigureAwait(false);

        var options = ctx.ContractOptions ?? RoundContractOptions.Default;
        var judgeSteps = ctx.Failure is null && string.IsNullOrWhiteSpace(ctx.Error);
        var executed = ctx.ExecutedSteps
            .Concat(result.Records.Select(static r => r.NodeKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var verdict = RoundContractJudge.Judge(spec, result.Records, executed, result.SkippedSteps, options, judgeSteps);

        // Throw 档：契约缺陷即抛（动作唯一出口 = JudgmentEngine.Apply；不收割，避免把已判定为非法的现场当成果落库）。
        if (options.Strictness == RoundContractStrictness.Throw && verdict.HasViolations)
        {
            JudgmentEngine.Apply(verdict.ToJudgmentVerdict(), options.Strictness);
        }

        var terminal = RoundTerminalPolicy.Evaluate(
            spec, result, ctx, _decisionMapper is null ? null : d => _decisionMapper(d),
            verdict, _terminalOptions with { Strictness = options.Strictness });

        if (_statePort is not null && spec.HarvestPolicy != RoundHarvestPolicy.None)
            await _statePort.HarvestAsync(spec, ctx, ct).ConfigureAwait(false);

        var violations = new List<string>(result.ContractViolations ?? Array.Empty<string>());
        violations.AddRange(verdict.ToViolations());

        return result with
        {
            Terminal = terminal,
            Success = terminal.Kind == RoundTerminalKind.Succeeded,
            Error = terminal.Failure?.Message ?? result.Error,
            ContractViolations = violations,
        };
    }
}
