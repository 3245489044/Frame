#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Gather;

/// <summary>数据源端口（域内唯一实现）：按 Need 取数，Hub 不感知域语义。</summary>
public interface IGatherProducer
{
    /// <summary>按需取数；返回 null 亦为合法值（由调用方按三态处理）。</summary>
    Task<object?> ProduceAsync(GatherNeedSpec need, CancellationToken ct);
}

/// <summary>一个阶段的取数结果（可复算：实际取数域 / 复用域 / 耗时）。</summary>
public sealed record GatherStageResult(
    string Stage,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Gathered,
    IReadOnlyList<string> Reused,
    long ElapsedMs);

/// <summary>出口闭合违规（写未声明产物）—— 属判据失败，非可忽略异常。</summary>
public sealed class GatherClosureViolationException : InvalidOperationException
{
    public GatherClosureViolationException(string message) : base(message) { }
}

/// <summary>
/// 写回票据（C-OUT 结构级）：由 <see cref="GatherHub.Emit"/> 唯一产出，承载「哪个视图的哪个已声明产物 + 载荷」。
/// <para>唯一 writer 只接受票据 ⇒ 未声明产物在结构上写不出来（不再依赖调用方自觉）。</para>
/// </summary>
/// <param name="View">产出该载荷的视图名。</param>
/// <param name="Produce">命中的产物声明（出口闭合判据载体）。</param>
/// <param name="Payload">载荷（写入内容；非空）。</param>
public sealed record GatherEmitTicket(string View, GatherProduceSpec Produce, object Payload);

/// <summary>
/// 聚拢 Hub —— 唯一读写面（C-IN / C-OUT / C-DEP）：
/// <list type="bullet">
/// <item>C-IN：取数只经 <see cref="GatherAsync"/>（生产者在 <see cref="IGatherProducer"/> 内实现）；</item>
/// <item>C-OUT：写出只经 <see cref="Emit"/>，未声明产物 ⇒ 抛；</item>
/// <item>C-DEP：阶段执行序 = <c>deps</c> 拓扑序（声明面禁步骤词）。</item>
/// </list>
/// <para>复用键 = <c>(Domain, Scope)</c>（<see cref="CrossScope"/>=false 默认），消除跨作用域影子缓存。</para>
/// </summary>
public sealed class GatherHub
{
    private readonly Dictionary<(string Domain, string Scope), object?> _snapshot = new();
    private readonly GatherRegistry? _registry;

    /// <summary>自由装配（兼容期）：生产者由调用方逐次提供。</summary>
    public GatherHub()
    {
    }

    /// <summary>注册表装配（loc 反转）：域 → location → 实现全部来自声明面，宿主无 switch。</summary>
    public GatherHub(GatherRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>是否允许跨作用域复用（默认 false：影子缓存视为缺陷）。</summary>
    public bool CrossScope { get; init; }

    /// <summary>单阶段取数（按 Need 顺序；未变更作用域且声明 Reusable 时复用上拍快照）。</summary>
    public async Task<GatherStageResult> GatherAsync(
        GatherStageSpec stage,
        IReadOnlyList<GatherNeedSpec> needs,
        IGatherProducer producer,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(producer);

        var changed = changedScopes is null
            ? null
            : new HashSet<string>(changedScopes, StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, object?>(stage.Needs.Count, StringComparer.Ordinal);
        var gathered = new List<string>(stage.Needs.Count);
        var reused = new List<string>();

        var watch = Stopwatch.StartNew();
        foreach (var domain in stage.Needs)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            var need = FindNeed(needs, domain)
                       ?? throw new InvalidOperationException(
                           $"阶段 '{stage.Name}' 引用未声明 need '{domain}'（声明与 producer 表不闭合）");

            var unchanged = changed is not null && !changed.Contains(need.Scope);
            var key = CacheKey(need);
            if (need.Reusable && unchanged && _snapshot.TryGetValue(key, out var cached))
            {
                values[need.Domain] = cached;
                reused.Add(need.Domain);
                continue;
            }

            var value = await producer.ProduceAsync(need, ct).ConfigureAwait(false);
            values[need.Domain] = value;
            _snapshot[key] = value;
            gathered.Add(need.Domain);
        }

        watch.Stop();
        return new GatherStageResult(stage.Name, values, gathered, reused, watch.ElapsedMilliseconds);
    }

    /// <summary>按注册表取数（loc 反转）：域 → location → 实现，宿主不再自建 switch。</summary>
    public Task<GatherStageResult> GatherAsync(
        GatherStageSpec stage,
        IReadOnlyList<GatherNeedSpec> needs,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        var registry = _registry
                       ?? throw new GatherClosureViolationException(
                           "未装配 GatherRegistry（loc 反转要求先 Build 并注入模块）");
        return GatherAsync(stage, needs, new RegistryProducer(registry), changedScopes, ct);
    }

    /// <summary>强类型取数（禁裸字符串）：命中本次已取快照；未取过 ⇒ default（调用方须显式判空）。</summary>
    public T? Get<T>(GatherToken<T> token)
        => _snapshot.TryGetValue((token.Domain, token.Scope), out var value) && value is T typed ? typed : default;

    /// <summary>产物令牌版出口闭合判定。</summary>
    public GatherEmitTicket Emit(GatherViewSpec view, GatherProduceToken token, object payload)
        => Emit(view, token.Name, payload);

    /// <summary>按注册表执行全视图（loc 反转：域 → location → 实现；阶段序 = deps 拓扑序）。</summary>
    public Task<IReadOnlyList<GatherStageResult>> RunAsync(
        GatherViewSpec view,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        var registry = _registry
                       ?? throw new GatherClosureViolationException(
                           "未装配 GatherRegistry（loc 反转要求先 Build 并注入模块）");
        return RunAsync(view, new RegistryProducer(registry), changedScopes, ct);
    }

    /// <summary>按 deps 拓扑序执行全部阶段（同阶段内 need 顺序 = 声明序）。</summary>
    public async Task<IReadOnlyList<GatherStageResult>> RunAsync(
        GatherViewSpec view,
        IGatherProducer producer,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ordered = Order(view);
        var results = new List<GatherStageResult>(ordered.Count);
        foreach (var stage in ordered)
        {
            results.Add(await GatherAsync(stage, view.Needs, producer, changedScopes, ct).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// 出口闭合（C-OUT）：写回必须命中视图的 produces 名单，否则抛。
    /// <para>返回 <see cref="GatherEmitTicket"/>（结构级凭证）：唯一 writer 只收票据，裸 payload 不构成写回许可。</para>
    /// </summary>
    public GatherEmitTicket Emit(GatherViewSpec view, string produceName, object payload)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(payload);
        var produce = view.FindProduce(produceName)
                      ?? throw new GatherClosureViolationException(
                          $"未声明产物 '{produceName}'（视图 '{view.View}'）：写出必须命中 produces 名单（C-OUT）");
        return new GatherEmitTicket(view.View, produce, payload);
    }

    /// <summary>丢弃上拍快照（作用域切换 / 强制刷新）。</summary>
    public void Reset() => _snapshot.Clear();

    private (string Domain, string Scope) CacheKey(GatherNeedSpec need)
        => CrossScope ? ("*", need.Domain) : (need.Domain, need.Scope);

    /// <summary>注册表 → 生产者适配（loc 反转：域 → location → 实现）。</summary>
    private sealed class RegistryProducer : IGatherProducer
    {
        private readonly GatherRegistry _registry;

        internal RegistryProducer(GatherRegistry registry) => _registry = registry;

        public Task<object?> ProduceAsync(GatherNeedSpec need, CancellationToken ct)
            => _registry.Producer(_registry.LocationOf(need.Domain))(need, ct);
    }

    private static GatherNeedSpec? FindNeed(IReadOnlyList<GatherNeedSpec> needs, string domain)
    {
        foreach (var need in needs)
        {
            if (string.Equals(need.Domain, domain, StringComparison.Ordinal))
            {
                return need;
            }
        }

        return null;
    }

    private static IReadOnlyList<GatherStageSpec> Order(GatherViewSpec view)
    {
        var byName = new Dictionary<string, GatherStageSpec>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var stage in view.Stages)
        {
            byName[stage.Name] = stage;
            indegree.TryAdd(stage.Name, 0);
            dependents.TryAdd(stage.Name, new List<string>());
        }

        foreach (var stage in view.Stages)
        {
            foreach (var dep in stage.Deps)
            {
                if (!byName.ContainsKey(dep))
                {
                    continue; // 装载期已判红；这里不重复裁决
                }

                indegree[stage.Name]++;
                dependents[dep].Add(stage.Name);
            }
        }

        var ready = new List<string>();
        foreach (var stage in view.Stages)
        {
            if (indegree[stage.Name] == 0)
            {
                ready.Add(stage.Name);
            }
        }

        var ordered = new List<GatherStageSpec>(view.Stages.Count);
        while (ready.Count > 0)
        {
            var name = ready[0];
            ready.RemoveAt(0);
            ordered.Add(byName[name]);

            foreach (var next in dependents[name])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        if (ordered.Count != view.Stages.Count)
        {
            throw new InvalidOperationException($"视图 '{view.View}' 的阶段图存在环（拓扑不可解）");
        }

        return ordered;
    }
}
