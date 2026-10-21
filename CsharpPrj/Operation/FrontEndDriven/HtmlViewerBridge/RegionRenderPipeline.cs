#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 区域渲染流水线（通用件）—— 把「本拍视图快照 → 稳定/易变指纹短路 → 区域片段 → 键控树/易变叶子写计划 → 产物」
/// 收拢为单一实现，领域只提供快照、指纹字段声明、区域渲染器与叶子计划。
/// <para>
/// 语义与既有领域实现逐条对齐：稳定输入未变 ⇒ 0 渲染 0 写回；区域内容逐字未变 ⇒ 不产写目标；
/// 仅易变叶子变化 ⇒ 产出定点叶子计划；渲染异常 ⇒ 显式 Skip（不整页兜底）。
/// </para>
/// <para>状态（最近快照 + 去重水位）由本类持有，领域服务不再自持「上一拍输入」字段。</para>
/// </summary>
/// <typeparam name="TSnapshot">本拍渲染输入快照（引用类型）。</typeparam>
public sealed class RegionRenderPipeline<TSnapshot> where TSnapshot : class
{
    /// <summary>区域渲染委托：快照 + 受控展开键 → 区域片段（区域键 → HTML）。</summary>
    public delegate Task<IReadOnlyDictionary<string, string>> RegionRenderer(
        TSnapshot snapshot, IReadOnlyList<string>? openKeys, CancellationToken ct);

    /// <summary>易变叶子计划委托：快照 → 叶子写计划（null = 无定点计划）。</summary>
    public delegate UiWritePlan? VolatileLeafPlanner(TSnapshot snapshot);

    private readonly IRefreshStore _store;
    private readonly Func<string, string> _hash;
    private readonly Func<TSnapshot, string> _stableFingerprint;
    private readonly Func<TSnapshot, string> _volatileFingerprint;
    private readonly RegionRenderer _renderRegions;
    private readonly VolatileLeafPlanner _leaves;
    private readonly string _stableSlotKey;
    private readonly string _volatileSlotKey;
    private readonly string _regionSlotPrefix;
    private readonly string _regionNamePrefix;
    private readonly Action<string, Exception>? _onFailure;
    private TSnapshot? _last;

    public RegionRenderPipeline(
        IRefreshStore store,
        Func<string, string> hash,
        Func<TSnapshot, string> stableFingerprint,
        Func<TSnapshot, string> volatileFingerprint,
        RegionRenderer renderRegions,
        VolatileLeafPlanner leaves,
        string stableSlotKey,
        string volatileSlotKey,
        string regionSlotPrefix,
        string regionNamePrefix = HtmlShellProjector.RegionNamePrefix,
        Action<string, Exception>? onFailure = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hash = hash ?? throw new ArgumentNullException(nameof(hash));
        _stableFingerprint = stableFingerprint ?? throw new ArgumentNullException(nameof(stableFingerprint));
        _volatileFingerprint = volatileFingerprint ?? throw new ArgumentNullException(nameof(volatileFingerprint));
        _renderRegions = renderRegions ?? throw new ArgumentNullException(nameof(renderRegions));
        _leaves = leaves ?? throw new ArgumentNullException(nameof(leaves));
        _stableSlotKey = stableSlotKey;
        _volatileSlotKey = volatileSlotKey;
        _regionSlotPrefix = regionSlotPrefix;
        _regionNamePrefix = regionNamePrefix;
        _onFailure = onFailure;
    }

    /// <summary>记录本拍快照（渲染装配路径调用；供刷新/显式路径复用，避免重复取数）。</summary>
    public void Track(TSnapshot snapshot) => _last = snapshot;

    /// <summary>最近一次快照；未渲染过 ⇒ false。</summary>
    public bool TryGetLast(out TSnapshot snapshot)
    {
        if (_last is { } value)
        {
            snapshot = value;
            return true;
        }

        snapshot = null!;
        return false;
    }

    /// <summary>
    /// 构建写产物：<paramref name="fingerprintShortCircuit"/>=false（显式交互）时不做稳定输入短路。
    /// </summary>
    public async Task<RenderOutput> BuildAsync(
        bool fingerprintShortCircuit,
        IReadOnlyList<string>? openKeys,
        UiInteractionSnapshot? interaction,
        string? traceId,
        CancellationToken ct)
    {
        if (_last is not { } snapshot) return RenderOutput.Skip;

        if (fingerprintShortCircuit && Unchanged(_stableSlotKey, _stableFingerprint(snapshot)))
        {
            // 稳定输入未变 ⇒ 区域不重渲染；仅易变叶子可能变化（心跳定点更新路径）。
            if (Unchanged(_volatileSlotKey, _volatileFingerprint(snapshot))) return RenderOutput.Skip;
            var heartbeatLeaves = _leaves(snapshot);
            return heartbeatLeaves is null ? RenderOutput.Skip : RenderOutput.Write(heartbeatLeaves);
        }

        IReadOnlyDictionary<string, string> regions;
        try
        {
            regions = await _renderRegions(snapshot, openKeys, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 渲染异常 ⇒ 显式不写，但必须可观测（不得静默吞）。
            _onFailure?.Invoke("region-render-failed", ex);
            return RenderOutput.Skip;
        }

        // 易变指纹在本拍只裁决一次（与稳定指纹同口径，避免「叶子计划永不可达」）。
        var volatileChanged = !Unchanged(_volatileSlotKey, _volatileFingerprint(snapshot));

        var trees = new List<UiWriteTarget>(regions.Count);
        foreach (var region in regions)
        {
            if (Unchanged(_regionSlotPrefix + region.Key, _hash(region.Value))) continue;
            var parsed = UiStateBinder.Bind(HtmlNodeParser.Parse(region.Value), openKeys);
            trees.Add(new UiWriteTarget(_regionNamePrefix + region.Key, parsed));
        }

        // 易变叶子与区域树同拍产出（树先落地、叶子后定点），不再因「区域非空」被丢弃。
        var leafPlan = volatileChanged ? _leaves(snapshot) : null;
        if (trees.Count == 0)
        {
            return leafPlan is null ? RenderOutput.Skip : RenderOutput.Write(leafPlan);
        }

        if (leafPlan?.Targets is { Count: > 0 } leaves)
        {
            return RenderOutput.Write(UiWritePlan.TreesAndLeaves(trees, leaves, interaction, traceId));
        }

        return RenderOutput.Write(UiWritePlan.Trees(trees, interaction, null, traceId));
    }

    /// <summary>内容未变 ⇒ true（不触写回）；变了 ⇒ 记入框架单一状态源并返回 false。</summary>
    private bool Unchanged(string slotKey, string fingerprint)
    {
        var version = _store.NextVersion(slotKey);
        if (_store.Decide(slotKey, version, fingerprint) == AcceptDecision.Skip) return true;
        _store.Commit(slotKey, version, fingerprint);
        return false;
    }
}
