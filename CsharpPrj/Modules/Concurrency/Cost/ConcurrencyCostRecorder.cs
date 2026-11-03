using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>
/// 并发组成本收口：组结算后为每个成员产 cost span（幂等键 = groupKey:member），
/// 并用实际用量校准估算（EWMA）。账本经端口注入（不新增表/账本）；未知金额保持 null（禁以 0 冒充）。
/// </summary>
public sealed class ConcurrencyCostRecorder
{
    private const string Schema = "concurrency-group.v1";
    private const double EwmaAlpha = 0.3;
    private readonly IConcurrencyCostSink? _sink;
    private readonly IConcurrencyUsageQuery? _query;
    private readonly ConcurrencyOptions _options;
    private readonly ConcurrentDictionary<string, double> _unitTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> _reuseRatio = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unstable = new(StringComparer.Ordinal);

    public ConcurrencyCostRecorder(
        ConcurrencyOptions options, IConcurrencyCostSink? sink, IConcurrencyUsageQuery? query)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sink = sink;
        _query = query;
    }

    /// <summary>组结算后写成员 span + 校准（workflow 缺省 = group 前缀）。</summary>
    public async Task RecordGroupAsync(
        ConcurrencyGroupSettlement settlement, string? workflow = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (_sink is null && _query is null) return;

        var groupKey = settlement.GroupKey;
        var workflowId = string.IsNullOrWhiteSpace(workflow) ? WorkflowOf(groupKey) : workflow!;
        long estimatedTotal = 0;
        long actualTotal = 0;

        for (var i = 0; i < settlement.Members.Count; i++)
        {
            var member = settlement.Members[i];
            var estimate = member.EstimatedTokens;
            estimatedTotal += Math.Max(0, estimate);

            long? actualTokens = member.ActualTokens;
            if (_query is not null)
            {
                try
                {
                    var usage = await _query.GetUsageAsync(member.TaskKey, ct).ConfigureAwait(false);
                    var total = usage.TokenIn + usage.TokenOut;
                    if (total > 0) actualTokens = total;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // 查询失败不阻断结算；amount 保持 Unknown（可见化由 cost 报告承担）。
                }
            }

            if (actualTokens is > 0) actualTotal += actualTokens.Value;

            if (_sink is null) continue;
            var span = new ConcurrencyCostSpan(
                Schema,
                IdempotencyKey: $"{groupKey}:{member.TaskKey}",
                GroupKey: groupKey,
                PlanKey: settlement.PlanKey,
                TaskKey: member.TaskKey,
                StartedAt: settlement.StartedAt.ToString("O"),
                DurationMs: (long)Math.Max(0, member.DurationMs),
                InputTokens: actualTokens is null && estimate > 0 ? estimate : null,
                OutputTokens: actualTokens,
                // ERR-16：估计/实际不混写——有实际用量即整条按实际，否则整条按估计。
                TotalTokens: actualTokens ?? (estimate > 0 ? estimate : null),
                UsageSource: actualTokens is null ? "Estimated" : "Reconciled",
                Currency: "USD",
                Attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["group_key"] = groupKey,
                    ["member_key"] = member.TaskKey,
                    ["plan_key"] = settlement.PlanKey ?? string.Empty,
                });

            try
            {
                await _sink.AppendAsync(span, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // 账本追加失败不阻断组结果（重放由幂等键去重）。
            }
        }

        if (actualTotal > 0)
            Observe(workflowId, settlement, estimatedTotal, actualTotal);
    }

    private void Observe(string workflow, ConcurrencyGroupSettlement settlement, long estimated, long actual)
    {
        if (estimated <= 0 || actual <= 0) return;

        var unit = actual / (double)Math.Max(1, settlement.Members.Count);
        _unitTokens.AddOrUpdate(workflow, unit, (_, previous) => Ewma(previous, unit));

        // ERR-18：复用率只由租约命中驱动（Reused / 全部租约事件）；估计偏差不再倒推复用率。
        var events = settlement.Reuse.Executed + settlement.Reuse.Waited + settlement.Reuse.Reused;
        var ratio = events <= 0 ? 0 : Math.Clamp((double)settlement.Reuse.Reused / events, 0, 1);
        _reuseRatio.AddOrUpdate(workflow, ratio, (_, previous) => Ewma(previous, ratio));

        var tolerance = Math.Max(0, _options.TaskComplexity.CalibrationTolerance);
        var deviation = Math.Abs(estimated - actual) / (double)Math.Max(1, actual);
        if (deviation > tolerance) _unstable[workflow] = true;
    }

    private static double Ewma(double previous, double current)
        => EwmaAlpha * current + (1 - EwmaAlpha) * previous;

    /// <summary>校准桶 = 组键前缀（ERR-19：不再把所有工作流混成一个固定桶）。</summary>
    private static string WorkflowOf(string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey)) return "concurrency-group";
        var idx = groupKey.IndexOf(':');
        return idx > 0 ? groupKey[..idx] : groupKey;
    }

    /// <summary>估算不稳定（偏差超 CalibrationTolerance）⇒ 后续保守不并发。</summary>
    public bool IsEstimatingUnstable(string? workflow)
        => !string.IsNullOrWhiteSpace(workflow) && _unstable.ContainsKey(workflow!);

    public double UnitTokens(string? workflow)
        => workflow is not null && _unitTokens.TryGetValue(workflow, out var value) ? value : 0;

    public double ReuseRatio(string? workflow)
        => workflow is not null && _reuseRatio.TryGetValue(workflow, out var value) ? value : 0;
}
