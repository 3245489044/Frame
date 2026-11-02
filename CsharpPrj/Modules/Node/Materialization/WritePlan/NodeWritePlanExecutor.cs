using Modules.Node.Diagnostics;

namespace Modules.Node.Materialization.WritePlan;

/// <summary>
/// 写计划执行器（唯一编排点）：<c>校验 → 稳定拓扑序 → 短路 → 每步在写门内应用 → 失败整体拒绝 + 逆序补偿</c>。
/// <para>
/// 复用（不复制）：
/// <list type="bullet">
///   <item>并发/单写：<see cref="NodeWriteGate"/>（target = <c>medium|collection|key</c>）；</item>
///   <item>补偿：介质处理器自持补偿动作（框架只负责逆序编排）；</item>
///   <item>介质实现：<see cref="IWriteMediumHandler"/>（宿主注入，框架零介质词）。</item>
/// </list>
/// </para>
/// <para>不做假事务：本执行器提供的是「写门串行 + 逆序补偿」，不冒充单事务。</para>
/// </summary>
public sealed class NodeWritePlanExecutor
{
    private readonly Dictionary<(string Medium, string Collection), IWriteMediumHandler> _handlers;
    private readonly NodeWriteGate _gate;
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly bool _requireLlmFree;

    /// <param name="handlers">介质处理器集（同 <c>(Medium, Collection)</c> 重复 ⇒ fail-fast）。</param>
    /// <param name="gate">写门原语（可空 ⇒ 自建 <c>write-plan</c> 门）。</param>
    /// <param name="requireLlmFree">是否要求所有步骤 LLM-free（缺省 false）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public NodeWritePlanExecutor(
        IEnumerable<IWriteMediumHandler> handlers,
        NodeWriteGate? gate = null,
        bool requireLlmFree = false,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _handlers = BuildHandlerMap(handlers);
        _gate = gate ?? new NodeWriteGate("write-plan", diagnostics: diagnostics);
        _requireLlmFree = requireLlmFree;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    /// <summary>已装配的写端口清单（装配断言/诊断用；每集合恰一个写者）。</summary>
    public IReadOnlyList<string> WritePorts
        => _handlers.Keys
            .OrderBy(static key => key.Medium, StringComparer.Ordinal)
            .ThenBy(static key => key.Collection, StringComparer.Ordinal)
            .Select(static key => $"{key.Medium}/{key.Collection}")
            .ToArray();

    /// <summary>应用一个写计划。任一步失败 → 计划整体拒绝（前序已应用步骤按逆序补偿）。</summary>
    public async Task<NodeWriteOutcome> ApplyAsync(NodeWritePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Steps.Count == 0)
        {
            return Reject(plan.Id, "write.plan-empty", "写计划为空：整体拒绝（不伪造成功）");
        }

        var ordering = TryOrder(plan);
        if (!ordering.Success)
        {
            return Reject(plan.Id, "write.plan-invalid", ordering.Error ?? "写计划非法");
        }

        var order = ordering.Order;

        var byId = order.ToDictionary(static work => work.Id, StringComparer.Ordinal);
        var skipped = plan.Skips.Select(static skip => skip.WorkId).ToArray();
        foreach (var skipId in skipped)
        {
            if (!byId.ContainsKey(skipId))
            {
                return Reject(plan.Id, "write.plan-invalid", $"短路声明指向不存在的步骤：{skipId}");
            }
        }

        var skippedSet = new HashSet<string>(skipped, StringComparer.Ordinal);
        var applied = new List<string>();
        var skippedApplied = new List<string>();
        var diagnostics = new List<NodeDiagnostic>();

        foreach (var work in order)
        {
            if (skippedSet.Contains(work.Id))
            {
                skippedApplied.Add(work.Id);
                continue;
            }

            if (_requireLlmFree && !work.LlmFree)
            {
                var compensated = await CompensateAsync(order, applied, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
                diagnostics.Add(StepDiagnostic("write.llm-free-violated", work, "步骤声明为 LLM-free 之外"));
                ReportAll(diagnostics);
                return NodeWriteOutcome.Rejected(
                    plan.Id, applied, skippedApplied, compensated, work.Id, "LLM-free 断言失败", diagnostics);
            }

            if (!_handlers.TryGetValue((work.Medium, work.Collection), out var handler))
            {
                var compensated = await CompensateAsync(order, applied, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
                diagnostics.Add(StepDiagnostic(
                    "write.plan-handler-missing", work, $"步骤 '{work.Id}' 的介质端口未装配"));
                ReportAll(diagnostics);
                return NodeWriteOutcome.Rejected(
                    plan.Id, applied, skippedApplied, compensated, work.Id, "介质端口未装配", diagnostics);
            }

            try
            {
                await _gate.RunAsync(TargetOf(work), () => handler.ApplyAsync(work, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                applied.Add(work.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 取消不是“步骤失败”：按取消语义向上传播，禁吞成 Rejected + 补偿。
                throw;
            }
            catch (Exception ex)
            {
                var compensated = await CompensateAsync(order, applied, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
                diagnostics.Add(StepDiagnostic("write.step-failed", work, ex.Message));
                ReportAll(diagnostics);
                return NodeWriteOutcome.Rejected(
                    plan.Id, applied, skippedApplied, compensated, work.Id, ex.Message, diagnostics);
            }
        }

        ReportAll(diagnostics);
        return NodeWriteOutcome.Ok(plan.Id, applied, skippedApplied, diagnostics);
    }

    private static Dictionary<(string Medium, string Collection), IWriteMediumHandler> BuildHandlerMap(
        IEnumerable<IWriteMediumHandler> handlers)
    {
        var map = new Dictionary<(string Medium, string Collection), IWriteMediumHandler>();
        foreach (var handler in handlers ?? Array.Empty<IWriteMediumHandler>())
        {
            if (handler is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(handler.Medium) || handler.Collections.Count == 0)
            {
                throw new InvalidOperationException("介质处理器须声明非空 Medium 与至少一个 Collection");
            }

            foreach (var collection in handler.Collections)
            {
                if (string.IsNullOrWhiteSpace(collection))
                {
                    throw new InvalidOperationException($"介质 '{handler.Medium}' 声明了空集合名");
                }

                if (!map.TryAdd((handler.Medium, collection), handler))
                {
                    throw new InvalidOperationException(
                        $"写端口重复装配：{handler.Medium}/{collection} 已有写者（每集合恰一个唯一写端口）。");
                }
            }
        }

        return map;
    }

    /// <summary>按依赖稳定拓扑排序（声明序为 tie-break）；环/缺依赖/字段缺失 ⇒ 失败 + 原因。</summary>
    private static (bool Success, List<NodeWriteWork> Order, string? Error) TryOrder(NodeWritePlan plan)
    {
        var order = new List<NodeWriteWork>(plan.Steps.Count);
        string? error = null;

        var byId = new Dictionary<string, NodeWriteWork>(StringComparer.Ordinal);
        foreach (var work in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(work.Id))
            {
                return (false, order, "写步骤缺 Id（计划内唯一键）");
            }

            if (string.IsNullOrWhiteSpace(work.Medium) || string.IsNullOrWhiteSpace(work.Collection))
            {
                return (false, order, $"写步骤 '{work.Id}' 缺 Medium/Collection");
            }

            if (work.PayloadJson is null)
            {
                return (false, order, $"写步骤 '{work.Id}' 载荷为 null");
            }

            if (!byId.TryAdd(work.Id, work))
            {
                return (false, order, $"写步骤 Id 重复：{work.Id}");
            }
        }

        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(NodeWriteWork work)
        {
            if (done.Contains(work.Id))
            {
                return true;
            }

            if (!visiting.Add(work.Id))
            {
                error = $"写计划存在依赖环：{work.Id}";
                return false;
            }

            foreach (var dependency in work.Dependencies)
            {
                if (!byId.TryGetValue(dependency, out var previous))
                {
                    error = $"写步骤 '{work.Id}' 依赖不存在的前序 '{dependency}'";
                    return false;
                }

                if (!Visit(previous))
                {
                    return false;
                }
            }

            visiting.Remove(work.Id);
            done.Add(work.Id);
            order.Add(work);
            return true;
        }

        foreach (var work in plan.Steps)
        {
            if (!Visit(work))
            {
                return (false, order, error ?? "写计划非法");
            }
        }

        return (true, order, null);
    }

    /// <summary>按逆序补偿已应用步骤（补偿失败不掩盖原始失败，仅留痕）。</summary>
    private async Task<IReadOnlyList<string>> CompensateAsync(
        IReadOnlyList<NodeWriteWork> order,
        IReadOnlyList<string> applied,
        List<NodeDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (applied.Count == 0)
        {
            return Array.Empty<string>();
        }

        var appliedSet = new HashSet<string>(applied, StringComparer.Ordinal);
        var compensated = new List<string>();

        for (var index = order.Count - 1; index >= 0; index--)
        {
            var work = order[index];
            if (!appliedSet.Contains(work.Id))
            {
                continue;
            }

            if (!_handlers.TryGetValue((work.Medium, work.Collection), out var handler))
            {
                continue;
            }

            try
            {
                await handler.CompensateAsync(work, cancellationToken).ConfigureAwait(false);
                compensated.Add(work.Id);
            }
            catch (Exception ex)
            {
                diagnostics.Add(StepDiagnostic("write.compensate-failed", work, ex.Message));
            }
        }

        return compensated;
    }

    private static string TargetOf(NodeWriteWork work)
        => $"{work.Medium}|{work.Collection}|{work.Key ?? work.Id}";

    private static NodeDiagnostic StepDiagnostic(string code, NodeWriteWork work, string message)
        => new(code, NodeDiagnosticSeverity.Error, message, $"{work.Medium}/{work.Collection}/{work.Id}");

    private NodeWriteOutcome Reject(string planId, string code, string message)
    {
        var diagnostic = new NodeDiagnostic(code, NodeDiagnosticSeverity.Error, message, planId);
        _diagnostics.Report(diagnostic);
        return NodeWriteOutcome.Rejected(
            planId, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, message,
            new[] { diagnostic });
    }

    private void ReportAll(IReadOnlyList<NodeDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }
    }
}
