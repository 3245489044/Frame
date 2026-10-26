#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UiBackend.Abstractions;

/// <summary>
/// 分域计时器（唯一实现）：把「同名域多次调用累加」与「按耗时降序单行描述」收敛到一处。
/// <para>领域只提供 phase 名称（histories/cost/prompt/...），机制不外泄。</para>
/// </summary>
public sealed class PhaseTimer
{
    private readonly Dictionary<string, long> _phases;

    /// <summary>以外部字典为承载（调用方需要继续按字典消费时使用，例如日志字段 <c>gatherPhases</c>）。</summary>
    public PhaseTimer(Dictionary<string, long>? phases = null)
        => _phases = phases ?? new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>以字典承载计时器（字典为 null 时返回 null，调用方可据此跳过计时分支）。</summary>
    public static PhaseTimer? Of(Dictionary<string, long>? phases)
        => phases is null ? null : new PhaseTimer(phases);

    /// <summary>分域耗时表（只读视图，直接消费 = 同一份账）。</summary>
    public IReadOnlyDictionary<string, long> Phases => _phases;

    /// <summary>同步段计时（同域多次调用求和）。</summary>
    public IDisposable Measure(string phase)
    {
        var started = Stopwatch.GetTimestamp();
        return new Scope(() => Accumulate(phase, started));
    }

    /// <summary>异步段计时（同域多次调用求和）；调用方 <c>using</c> 包住 await。</summary>
    public IDisposable MeasureAsync(string phase)
    {
        var started = Stopwatch.GetTimestamp();
        return new Scope(() => Accumulate(phase, started));
    }

    /// <summary>按耗时降序的单行描述；空账 ⇒ "-"（禁空串冒充"无耗时"）。</summary>
    public string Describe()
        => _phases.Count == 0
            ? "-"
            : string.Join(",", _phases.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}ms"));

    private void Accumulate(string phase, long started)
    {
        if (string.IsNullOrWhiteSpace(phase)) return;
        var ms = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _phases[phase] = _phases.TryGetValue(phase, out var prev) ? prev + ms : ms;
    }

    private sealed class Scope : IDisposable
    {
        private Action? _onDispose;

        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            var action = _onDispose;
            _onDispose = null;
            action?.Invoke();
        }
    }
}
