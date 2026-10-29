#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Gather;

namespace FrontEndDriven.HtmlHosting;

/// <summary>取数成本档（界判据见领域声明面；本层只承载枚举）。</summary>
public enum UiCostClass
{
    Low = 0,
    High = 1,
}

/// <summary>一个数据需求：域 + 作用域 + 成本档 + 是否可跨拍复用。</summary>
public readonly record struct UiDataNeed(
    string Domain,
    string Scope,
    UiCostClass Cost = UiCostClass.Low,
    bool Reusable = false);

/// <summary>视图声明：需要哪些域、产出哪些区域（执行器据此取数，不再写分支梯）。</summary>
public sealed record UiViewSpec(
    string View,
    IReadOnlyList<UiDataNeed> Needs,
    IReadOnlyList<string> Sections);

/// <summary>数据源端口：按需求取数（域内实现，执行器不感知域）。</summary>
public interface IUiViewDataSource
{
    Task<object?> GatherAsync(UiDataNeed need, CancellationToken ct);
}

/// <summary>一拍的取数结果：域值表 + 实际取数域 + 复用域（可判定：复用了哪些域）。</summary>
public sealed record UiViewGatherResult(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Gathered,
    IReadOnlyList<string> Reused);

/// <summary>
/// 视图投影执行器（通用件，唯一实现）：<c>ChangeScope → DataNeeds</c> 取数。
/// <para>
/// 本类现为 <see cref="GatherHub"/> 的**薄适配器**（保留既有签名，禁留第二套缓存/第二套取数语义）；
/// 未变更域 0 取数、0 写目标由「复用域」清单可复算；新增数据域只加声明，不改执行器。
/// </para>
/// </summary>
public sealed class UiViewProjection
{
    private readonly GatherHub _hub = new();

    /// <summary>
    /// 按注册表缓存的 Hub（E-4 修复）：同一装配只建一次，否则 <c>_snapshot</c> 恒空 ⇒ <c>Reusable</c> 形同关闭。
    /// <para>键 = <see cref="GatherRegistry"/> 引用（装配结果不可变）；<c>Lazy</c> 保证并发拍只建一个。</para>
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<GatherRegistry, Lazy<GatherHub>> _hubs = new();

    /// <summary>Hub 实例（上层按声明面 stages 编排时直接使用）。</summary>
    public GatherHub Hub => _hub;

    /// <summary>执行一拍取数；<paramref name="changedScopes"/> 为 null ⇒ 视为全量变更（禁静默跳过）。</summary>
    public async Task<UiViewGatherResult> GatherAsync(
        UiViewSpec spec,
        IUiViewDataSource source,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);

        var (needs, domains) = BuildNeeds(spec);
        var stage = new GatherStageSpec(spec.View, domains, Array.Empty<string>(), spec.Sections);
        var result = await _hub
            .GatherAsync(stage, needs, new DataSourceProducer(source), changedScopes, ct)
            .ConfigureAwait(false);

        return new UiViewGatherResult(result.Values, result.Gathered, result.Reused);
    }

    /// <summary>
    /// 按模块装配（**loc 反转**）：宿主只声明（<see cref="IGatherModule"/>），框架做闭合校验与执行。
    /// <para>未闭合（缺实现/孤儿实现/签名非法）⇒ 抛 <see cref="GatherClosureViolationException"/>，逐项列出。</para>
    /// </summary>
    public async Task<UiViewGatherResult> GatherAsync(
        UiViewSpec spec,
        IGatherModule module,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(module);

        var (needs, domains) = BuildNeeds(spec);
        var viewSpec = new GatherViewSpec(
            spec.View,
            Array.Empty<GatherStageSpec>(),
            needs,
            Array.Empty<GatherProduceSpec>());
        var registry = GatherRegistry.Build(new IGatherModule[] { module }, viewSpec);
        if (!registry.IsClosed)
        {
            throw new GatherClosureViolationException(
                $"聚拢未闭合（模块 '{module.Name}'）：{string.Join("；", registry.Issues)}");
        }

        var hub = BindHub(registry);
        var stage = new GatherStageSpec(spec.View, domains, Array.Empty<string>(), spec.Sections);
        var result = await hub.GatherAsync(stage, needs, changedScopes, ct).ConfigureAwait(false);
        return new UiViewGatherResult(result.Values, result.Gathered, result.Reused);
    }

    /// <summary>
    /// 整视图执行（**多阶段、按 deps 拓扑序**）：声明面（<see cref="GatherViewSpec"/>）+ 模块 ↔ 实现双向对账（R9/R10）。
    /// <para>与 <see cref="GatherAsync(UiViewSpec, IGatherModule, IReadOnlyCollection{string}?, CancellationToken)"/> 的差别：
    /// 后者为单阶段适配（既有视图），本方法跑完整 stage 图（管道型视图，如 conversation-canon）。</para>
    /// </summary>
    public async Task<IReadOnlyList<GatherStageResult>> RunAsync(
        GatherViewSpec view,
        IGatherModule module,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(module);

        var registry = GatherRegistry.Build(new IGatherModule[] { module }, view);
        if (!registry.IsClosed)
        {
            throw new GatherClosureViolationException(
                $"聚拢未闭合（视图 '{view.View}' / 模块 '{module.Name}'）：{string.Join("；", registry.Issues)}");
        }

        return await BindHub(registry).RunAsync(view, changedScopes, ct).ConfigureAwait(false);
    }

    /// <summary>出口闭合判定（C-OUT）：返回票据；未声明产物 ⇒ 抛。</summary>
    public GatherEmitTicket Emit(GatherViewSpec view, string produceName, object payload)
        => _hub.Emit(view, produceName, payload);

    private static (List<GatherNeedSpec> Needs, List<string> Domains) BuildNeeds(UiViewSpec spec)
    {
        var needs = new List<GatherNeedSpec>(spec.Needs.Count);
        var domains = new List<string>(spec.Needs.Count);
        foreach (var need in spec.Needs)
        {
            if (string.IsNullOrWhiteSpace(need.Domain))
            {
                continue;
            }

            needs.Add(new GatherNeedSpec(need.Domain, need.Scope, ToGatherCost(need.Cost), need.Reusable));
            domains.Add(need.Domain);
        }

        return (needs, domains);
    }

    /// <summary>丢弃上拍快照（作用域切换/强制刷新时调用）。</summary>
    public void Reset()
    {
        _hub.Reset();
        _hubs.Clear();
    }

    /// <summary>注册表 → 缓存单例 Hub（复用键恒 <c>(Domain, Scope)</c>，<c>CrossScope</c> 保持默认 false）。</summary>
    private GatherHub BindHub(GatherRegistry registry)
        => _hubs.GetOrAdd(
                registry,
                static r => new Lazy<GatherHub>(() => new GatherHub(r), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    /// <summary>数据源端口 → Hub 生产者（唯一适配点）。</summary>
    private sealed class DataSourceProducer : IGatherProducer
    {
        private readonly IUiViewDataSource _source;

        internal DataSourceProducer(IUiViewDataSource source) => _source = source;

        public Task<object?> ProduceAsync(GatherNeedSpec need, CancellationToken ct)
            => _source.GatherAsync(new UiDataNeed(need.Domain, need.Scope, ToUiCost(need.Cost), need.Reusable), ct);
    }

    /// <summary>宿主成本档 → 通用内核成本档（唯一映射点）。</summary>
    private static GatherCostClass ToGatherCost(UiCostClass cost)
        => cost == UiCostClass.High ? GatherCostClass.High : GatherCostClass.Low;

    /// <summary>通用内核成本档 → 宿主成本档（唯一映射点）。</summary>
    private static UiCostClass ToUiCost(GatherCostClass cost)
        => cost == GatherCostClass.High ? UiCostClass.High : UiCostClass.Low;
}
