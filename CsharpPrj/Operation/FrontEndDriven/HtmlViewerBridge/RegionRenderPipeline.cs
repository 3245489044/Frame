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
    private readonly string _streamRegionPrefix;
    private readonly string _streamName;
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
        Action<string, Exception>? onFailure = null,
        string streamRegionPrefix = HtmlPageRegionLayout.StreamSectionPrefix,
        // 流目标 DOM 名 = 壳体容器名（region-bubbles）；曾与段键前缀 "bubbles:" 混用 ⇒ 目标恒 NOT_FOUND（failed=[bubbles]）。
        string streamName = HtmlShellProjector.RegionNamePrefix + "bubbles")
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
        _streamRegionPrefix = streamRegionPrefix;
        _streamName = streamName;
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
    /// <paramref name="source"/> 为入口意图（Mount/Refresh/Interactive）；仅产易变叶子的分支降标为 <see cref="UiWriteSource.Tick"/>。
    /// 区域键以 <c>bubbles:</c> 前缀者按「流段」产出（同键定点更新、按流序追加），不进区域树。
    /// </summary>
    public async Task<RenderOutput> BuildAsync(
        bool fingerprintShortCircuit,
        IReadOnlyList<string>? openKeys,
        UiInteractionSnapshot? interaction,
        string? traceId,
        UiWriteSource source,
        CancellationToken ct)
    {
        if (_last is not { } snapshot) return RenderOutput.Skip;

        // 去重提交延迟到"计划已建成"之后：渲染失败/提前返回不推进 baseline（禁预提交）。
        var commits = new List<(string SlotKey, long Version, string Fingerprint)>();
        RenderOutput Done(RenderOutput output)
        {
            foreach (var c in commits) _store.Commit(c.SlotKey, c.Version, c.Fingerprint);
            return output;
        }

        if (fingerprintShortCircuit && !Changed(_stableSlotKey, _stableFingerprint(snapshot), commits))
        {
            // 稳定输入未变 ⇒ 区域不重渲染；仅易变叶子可能变化（心跳定点更新路径 ⇒ Tick）。
            if (!Changed(_volatileSlotKey, _volatileFingerprint(snapshot), commits)) return RenderOutput.Skip;
            var heartbeatLeaves = _leaves(snapshot);
            return heartbeatLeaves is null ? RenderOutput.Skip : Done(AsTick(heartbeatLeaves));
        }

        IReadOnlyDictionary<string, string> regions;
        try
        {
            regions = await _renderRegions(snapshot, openKeys, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 渲染异常 ⇒ 显式不写，但必须可观测（不得静默吞）；已决策的指纹不提交（下一拍重试）。
            _onFailure?.Invoke("region-render-failed", ex);
            return RenderOutput.Skip;
        }

        // 易变指纹在本拍只裁决一次（与稳定指纹同口径，避免「叶子计划永不可达」）。
        var volatileChanged = Changed(_volatileSlotKey, _volatileFingerprint(snapshot), commits);

        var trees = new List<UiWriteTarget>(regions.Count);
        var streamItems = new List<UiStreamItem>();
        foreach (var region in regions)
        {
            if (!Changed(_regionSlotPrefix + region.Key, _hash(region.Value), commits)) continue;
            if (region.Key.StartsWith(_streamRegionPrefix, StringComparison.Ordinal))
            {
                // 流段（气泡）：同键 ⇒ 定点更新；新键 ⇒ 按流序插入（方向由载荷声明 = OldestFirst）。
                streamItems.Add(new UiStreamItem(region.Key.Substring(_streamRegionPrefix.Length), region.Value));
                continue;
            }

            var parsed = UiStateBinder.Bind(HtmlNodeParser.Parse(region.Value), openKeys);
            trees.Add(new UiWriteTarget(_regionNamePrefix + region.Key, parsed));
        }

        var streams = new List<UiWriteTarget>(1);
        if (streamItems.Count > 0)
        {
            streams.Add(new UiWriteTarget(
                _streamName, new UiStreamAppend(streamItems, null, UiStreamDirection.OldestFirst)));
        }

        var leaves = volatileChanged ? _leaves(snapshot)?.Targets : null;
        var hasTrees = trees.Count > 0;
        var hasStreams = streams.Count > 0;
        var hasLeaves = leaves is { Count: > 0 };

        if (!hasTrees && !hasStreams && !hasLeaves) return Done(RenderOutput.Skip);
        if (hasTrees && !hasStreams && !hasLeaves)
            return Done(RenderOutput.Write(UiWritePlan.Trees(trees, interaction, null, traceId, source)));
        if (!hasTrees && hasStreams && !hasLeaves)
            return Done(RenderOutput.Write(UiWritePlan.AppendMany(streams, traceId, source)));
        if (!hasTrees && !hasStreams)
            // 仅易变叶子 ⇒ 心跳（Tick 判据载体 = 计划动词，非入口自述）。
            return Done(AsTick(UiWritePlan.Leaves(leaves!, interaction, traceId)));

        // 同拍组合（树 → 流 → 叶子）：单一计划、单一写入口。
        var composite = new List<UiWriteTarget>(trees.Count + streams.Count + (leaves?.Count ?? 0));
        composite.AddRange(trees);
        composite.AddRange(streams);
        if (hasLeaves) composite.AddRange(leaves!);
        return Done(RenderOutput.Write(UiWritePlan.Composite(composite, interaction, traceId, source)));
    }

    /// <summary>心跳产物唯一产地（Tick 只允许易变叶子）：动词非叶子即结构性违约 ⇒ 显式失败（禁静默降标）。
    /// 判据载体 = 计划动词（结构事实），非入口自述。</summary>
    private static RenderOutput AsTick(UiWritePlan plan)
    {
        if (plan.Kind != UiWriteKind.Leaf)
            throw new InvalidOperationException(
                $"Tick 计划只允许 Leaf 动词（实际={plan.Kind}；区域/树/流目标禁入 Tick 路径）");
        return RenderOutput.Write(plan with { Source = UiWriteSource.Tick });
    }

    /// <summary>内容变化判定：变化 ⇒ true 并把「待提交」记入 <paramref name="commits"/>（计划建成后才提交）。
    /// 未变/过期 ⇒ false（不触 DOM，也不推进 baseline）。</summary>
    private bool Changed(string slotKey, string fingerprint, List<(string SlotKey, long Version, string Fingerprint)> commits)
    {
        var version = _store.NextVersion(slotKey);
        if (_store.Decide(slotKey, version, fingerprint) == AcceptDecision.Skip) return false;
        commits.Add((slotKey, version, fingerprint));
        return true;
    }
}
