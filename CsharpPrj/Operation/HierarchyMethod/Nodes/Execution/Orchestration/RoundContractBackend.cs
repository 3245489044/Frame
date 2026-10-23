using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮契约装饰器（E1/E2/E3 单点落点）：把「seed → 执行 → 契约判定 → harvest」包成与后端无关的一层，
/// 使 <see cref="RoundCircuitBackend"/>（生产）与 <see cref="NodeGraphOrchestrator"/>（回退/直跑）同权，
/// 判据实现单源 = <see cref="DeclaredContractEnforcer"/>（禁后端各写一份）。
/// <para>enabled=false ⇒ 纯转发（行为回退到迁移前；seed/harvest 仍由内层自理，仅用于应急开关）。</para>
/// </summary>
public sealed class RoundContractBackend : IRoundExecutionBackend
{
    private readonly IRoundExecutionBackend _inner;
    private readonly IRoundStatePort? _statePort;
    private readonly bool _enabled;

    public RoundContractBackend(
        IRoundExecutionBackend inner,
        IRoundStatePort? statePort = null,
        bool enabled = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _statePort = statePort;
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

        var result = await _inner.RunRoundAsync(spec, ctx, ct).ConfigureAwait(false);

        var options = ctx.ContractOptions ?? RoundContractOptions.Default;
        var (stepMissing, portViolations) = Judge(spec, result, ctx, options);

        var ok = result.Success;
        var error = result.Error;
        var violations = new List<string>(result.ContractViolations ?? Array.Empty<string>());
        violations.AddRange(stepMissing);
        violations.AddRange(portViolations);

        // E1（既有语义）：步次缺口 ⇒ 该轮失败（Report 只改 ok/Error，Throw 立即抛）。
        if (stepMissing.Count > 0)
        {
            const string code = "exec.step-missing";
            error ??= $"{code}: [{string.Join(",", stepMissing)}]";
            ok = false;
            if (options.Strictness == RoundContractStrictness.Throw)
            {
                throw new InvalidOperationException(
                    $"{code}: 声明步骤未执行却判定成功（completed=[{string.Join(",", result.Records.Select(r => r.NodeKey))}], "
                    + $"missing=[{string.Join(",", stepMissing)}]）");
            }
        }

        // E2/E3：Report 档只可见化（不改 ok / 不写 Error）；Throw 档违规即抛。
        if (portViolations.Count > 0 && options.Strictness == RoundContractStrictness.Throw)
        {
            throw new InvalidOperationException(
                $"exec.contract: 端口契约未闭合（completed=[{string.Join(",", result.Records.Select(r => r.NodeKey))}], "
                + $"violations=[{string.Join(";", portViolations)}]）");
        }

        if (_statePort is not null)
            await _statePort.HarvestAsync(spec, ctx, ct).ConfigureAwait(false);

        return result with
        {
            Success = ok,
            Error = error,
            ContractViolations = violations,
        };
    }

    private static (IReadOnlyList<string> StepMissing, IReadOnlyList<string> PortViolations) Judge(
        RoundSpec spec, NodeRoundResult result, NodeRoundContext ctx, RoundContractOptions options)
    {
        var executed = ctx.ExecutedSteps
            .Concat(result.Records.Select(static r => r.NodeKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var declared = spec.Steps.Select(static s => s.EffectiveNodeId).ToArray();

        // 失败轮不判 E1（真实错误已归因，禁以缺步掩盖）。
        var stepMissing = result.Success
            ? DeclaredContractEnforcer.MissingSteps(declared, executed, result.SkippedSteps)
            : Array.Empty<string>();

        var stepsById = spec.Steps.ToDictionary(static s => s.EffectiveNodeId, StringComparer.Ordinal);
        var portViolations = new List<string>();
        foreach (var record in result.Records)
        {
            if (!stepsById.TryGetValue(record.NodeKey, out var step)) continue;

            if (record.InjectedKeys is { } injectedKeys)
            {
                var undeclaredIn = DeclaredContractEnforcer.Undeclared(
                    step.ParameterKeys, injectedKeys, options.InternalKeys);
                if (undeclaredIn.Count > 0)
                    portViolations.Add(
                        $"exec.undeclared-inport@{step.EffectiveNodeId}:[{string.Join(",", undeclaredIn)}]");
            }

            var undeclaredOut = DeclaredContractEnforcer.Undeclared(
                step.ResultKeys, record.OutputPorts.Keys, options.InternalKeys);
            if (undeclaredOut.Count > 0)
                portViolations.Add(
                    $"exec.undeclared-outport@{step.EffectiveNodeId}:[{string.Join(",", undeclaredOut)}]");
        }

        return (stepMissing, portViolations);
    }
}
