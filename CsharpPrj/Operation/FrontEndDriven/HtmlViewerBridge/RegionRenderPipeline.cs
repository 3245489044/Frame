#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Lease;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 区域渲染流水线（通用件，单帧装配）—— 把「本拍快照 → 单帧（视图键 + 模型）→ 稳定/易变指纹短路 →
/// 区域片段 → 键控树/流全集/易变叶子写计划 → 产物」收拢为单一实现。
/// <para>
/// 语义与既有领域实现逐条对齐：稳定输入未变 ⇒ 0 渲染 0 写回；区域内容逐字未变 ⇒ 不产写目标；
/// 仅易变叶子变化 ⇒ 产出定点叶子计划；渲染异常 ⇒ 显式 Skip（不整页兜底）。
/// </para>
/// <para>帧只装配一次（<c>UiViewFrame&lt;TModel&gt;</c>）：根片段与区域节点共用同一模型实例。</para>
/// <para>视图生命周期由 <see cref="UiViewHost"/> 单一裁决；流通道按「全集替换」表达 N↔0（无清空动词）。</para>
/// </summary>
/// <typeparam name="TSnapshot">本拍渲染输入快照（引用类型）。</typeparam>
/// <typeparam name="TFrame">渲染帧（视图键 + 已装配模型）。</typeparam>
public sealed class RegionRenderPipeline<TSnapshot, TFrame> where TSnapshot : class where TFrame : class
{
    /// <summary>区域渲染委托：帧 + 受控展开键 → 区域**节点**（区域键 → 节点树）。</summary>
    public delegate Task<IReadOnlyDictionary<string, UiNode>> RegionRenderer(
        TFrame frame, IReadOnlyList<string>? openKeys, CancellationToken ct);

    /// <summary>易变叶子计划委托：快照 → 叶子写计划（null = 无定点计划）。</summary>
    public delegate UiWritePlan? VolatileLeafPlanner(TSnapshot snapshot);

    private readonly IRefreshStore _store;
    private readonly Func<string, string> _hash;
    private readonly Func<TSnapshot, string> _stableFingerprint;
    private readonly Func<TSnapshot, string> _volatileFingerprint;
    private readonly Func<TSnapshot, TFrame> _frameOf;
    private readonly Func<TFrame, string?> _viewKeyOf;
    private readonly Func<TFrame, string> _rootRenderer;
    private readonly RegionRenderer _renderRegions;
    private readonly VolatileLeafPlanner _leaves;
    private readonly string _stableSlotKey;
    private readonly string _volatileSlotKey;
    private readonly string _regionSlotPrefix;
    private readonly Func<string>? _appearanceFingerprint;
    private readonly string _regionNamePrefix;
    private readonly string _streamRegionPrefix;
    private readonly Action<string, Exception>? _onFailure;
    private readonly UiViewHost _viewHost;
    private TSnapshot? _last;

    public RegionRenderPipeline(
        IRefreshStore store,
        Func<string, string> hash,
        Func<TSnapshot, string> stableFingerprint,
        Func<TSnapshot, string> volatileFingerprint,
        Func<TSnapshot, TFrame> frameOf,
        Func<TFrame, string?> viewKeyOf,
        Func<TFrame, string> rootRenderer,
        RegionRenderer renderRegions,
        VolatileLeafPlanner leaves,
        string stableSlotKey,
        string volatileSlotKey,
        string regionSlotPrefix,
        IUiDeclarationPort declarations,
        string regionNamePrefix = HtmlShellProjector.RegionNamePrefix,
        Action<string, Exception>? onFailure = null,
        string streamRegionPrefix = UiViewNames.StreamSectionPrefix,
        Func<string>? appearanceFingerprint = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hash = hash ?? throw new ArgumentNullException(nameof(hash));
        _stableFingerprint = stableFingerprint ?? throw new ArgumentNullException(nameof(stableFingerprint));
        _volatileFingerprint = volatileFingerprint ?? throw new ArgumentNullException(nameof(volatileFingerprint));
        _frameOf = frameOf ?? throw new ArgumentNullException(nameof(frameOf));
        _viewKeyOf = viewKeyOf ?? throw new ArgumentNullException(nameof(viewKeyOf));
        _rootRenderer = rootRenderer ?? throw new ArgumentNullException(nameof(rootRenderer));
        _renderRegions = renderRegions ?? throw new ArgumentNullException(nameof(renderRegions));
        _leaves = leaves ?? throw new ArgumentNullException(nameof(leaves));
        _stableSlotKey = stableSlotKey;
        _volatileSlotKey = volatileSlotKey;
        _regionSlotPrefix = regionSlotPrefix;
        _appearanceFingerprint = appearanceFingerprint;
        _regionNamePrefix = regionNamePrefix;
        _onFailure = onFailure;
        _streamRegionPrefix = streamRegionPrefix;

        // 视图程序目录必填且必须就绪（E2）：声明缺失/非法 ⇒ 构造期抛，禁 fail-open 静默退化。
        // 控制反转：目录由声明端口供给（组合根注入），本类不再持有任何解析/静态单例。
        ArgumentNullException.ThrowIfNull(declarations);
        declarations.EnsureReady();
        _viewHost = new UiViewHost(declarations.Blueprints, message => _onFailure?.Invoke(message, new InvalidOperationException(message)));
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
    /// 区域键以 <c>bubbles:</c> 前缀者按「流段」产出（全集替换：本拍该流的段集即写目标集），不进区域树。
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

        // 单帧装配（D3/D4）：视图键解析与模型装配各恰一次，根投影与区域节点共用。
        var frame = _frameOf(snapshot);
        var viewKey = _viewKeyOf(frame);

        // 视图作用域：切换判定由框架 ViewHost 单一裁决（卸载 = 根子集不含旧视图键，非清空动词）。
        var transition = _viewHost.Ensure(viewKey, _ => _rootRenderer(frame));

        // 状态迁移（VSC-4）：按目标视图声明裁剪交互态；丢弃诊断延后到「计划已采纳」后可见化（E4：
        // 渲染失败拍不重复产诊断，也不裁剪原始快照）。
        var migration = UiViewStateMigrationResult.None;
        if (transition.Changed)
        {
            UiViewBlueprint? target = _viewHost.Catalog.TryGet(viewKey, out var blueprint) ? blueprint : null;
            migration = UiViewStateMigrationPolicy.Apply(target, interaction);
            if (migration.Migrated is { } migrated) interaction = migrated;
        }

        // 根投影（首挂载/切换拍）：唯一目标 = 视图宿主；旧视图根由键控对齐按键删除。
        var roots = transition is { Changed: true, RootName: { Length: > 0 } rootName, RootHtml: { Length: > 0 } rootHtml }
            ? new List<UiWriteTarget>(1) { new(rootName, new UiRegionHtml(rootHtml)) }
            : new List<UiWriteTarget>(0);

        // 去重提交延迟到"计划已建成"之后：渲染失败/提前返回不推进 baseline（禁预提交）。
        var commits = new List<(string SlotKey, long Version, string Fingerprint)>();
        RenderOutput Done(RenderOutput output)
        {
            foreach (var c in commits) _store.Commit(c.SlotKey, c.Version, c.Fingerprint);
            foreach (var droppedKey in migration.Dropped)
            {
                _onFailure?.Invoke("view_state_dropped:" + droppedKey,
                    new InvalidOperationException("state dropped on view switch: " + droppedKey));
            }

            return output;
        }

        if (fingerprintShortCircuit && !transition.Changed
            && !Changed(StableSlot(viewKey), _stableFingerprint(snapshot), commits))
        {
            // 稳定输入未变 ⇒ 区域不重渲染；仅易变叶子可能变化（心跳定点更新路径 ⇒ Tick）。
            if (!Changed(VolatileSlot(viewKey), _volatileFingerprint(snapshot), commits)) return RenderOutput.Skip;
            var heartbeatLeaves = _leaves(snapshot);
            return heartbeatLeaves is null ? RenderOutput.Skip : Done(AsTick(heartbeatLeaves));
        }

        IReadOnlyDictionary<string, UiNode> regions;
        try
        {
            regions = await _renderRegions(frame, openKeys, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 渲染异常 ⇒ 显式不写，但必须可观测（不得静默吞）；已决策的指纹不提交（下一拍重试）。
            _onFailure?.Invoke("region-render-failed", ex);
            return RenderOutput.Skip;
        }

        // 易变指纹在本拍只裁决一次（与稳定指纹同口径，避免「叶子计划永不可达」）。
        var volatileChanged = Changed(VolatileSlot(viewKey), _volatileFingerprint(snapshot), commits);

        var trees = new List<UiWriteTarget>(regions.Count);
        var streamItems = new List<UiStreamItem>();
        var foldState = FoldBinder.ParseState(openKeys);
        foreach (var region in regions)
        {
            // 折叠态先绑定再算指纹/序列化：态变化必须进入变更判定（否则折叠交互被判「无变化」）。
            var bound = UiStateBinder.Bind(FoldBinder.Bind(region.Value, foldState), openKeys);
            if (region.Key.StartsWith(_streamRegionPrefix, StringComparison.Ordinal))
            {
                // 流段（气泡）：全集语义——本拍所有段一并给出，由流原语按键对齐（含删除未声明段）。
                streamItems.Add(new UiStreamItem(
                    region.Key.Substring(_streamRegionPrefix.Length), UiFragmentWriter.Serialize(bound)));
                continue;
            }

            if (!Changed(RegionSlot(viewKey, region.Key), _hash(UiFragmentWriter.Serialize(bound)), commits)) continue;

            // 节点直绑（不再把 HTML 解析回树：渲染产物本身即节点）；容器键已是 DOM 名（region-*）。
            trees.Add(new UiWriteTarget(TargetName(region.Key), bound));
        }

        var streams = new List<UiWriteTarget>(1);
        var streamContainer = StreamContainerFor(viewKey);
        if (streamContainer is not null)
        {
            // 流通道无段级增量：全集聚合成一个变更判定（否则全集替换会删除未声明但未变的旧段）。
            var setFingerprint = _hash(StreamSetKey(streamItems));
            if (Changed(StreamSlot(viewKey, streamContainer.Key), setFingerprint, commits))
                streams.Add(new UiWriteTarget(streamContainer.Key, UiStreamAppend.Set(streamItems)));
        }
        else if (streamItems.Count > 0)
        {
            // 产出流段但视图未声明流容器 ⇒ 判据不合格（写目标会落空/写孤儿），显式失败。
            throw new InvalidOperationException($"视图 '{viewKey}' 未声明流容器（region-bubbles 兜底已下线）");
        }

        var leaves = volatileChanged ? _leaves(snapshot)?.Targets : null;
        var hasRoots = roots.Count > 0;
        var hasTrees = trees.Count > 0;
        var hasStreams = streams.Count > 0;
        var hasLeaves = leaves is { Count: > 0 };

        // 空计划 ⇒ 无任何写目标，不得推进 baseline（契约：送达且变化才 Commit）。
        // 判据单源 = TickDecision（Modules，载荷无关）：禁在本类再写第二条「全空」判定。
        if (TickDecision.IsEmpty(hasRoots, hasTrees, hasStreams, hasLeaves)) return RenderOutput.Skip;

        if (hasRoots)
        {
            // 切换/首挂载拍：根投影 + 区域/流/叶子同拍单计划（根排首位：先立骨架再填内容）。
            var projected = new List<UiWriteTarget>(roots.Count + trees.Count + streams.Count + (leaves?.Count ?? 0));
            projected.AddRange(roots);
            projected.AddRange(trees);
            projected.AddRange(streams);
            if (hasLeaves) projected.AddRange(leaves!);
            _viewHost.Confirm(transition.To);
            return Done(RenderOutput.Write(UiWritePlan.Composite(projected, interaction, traceId, source)));
        }

        if (hasTrees && !hasStreams && !hasLeaves)
            return Done(RenderOutput.Write(UiWritePlan.Trees(trees, interaction, null, traceId, source)));
        if (!hasTrees && hasStreams && !hasLeaves)
            return Done(RenderOutput.Write(UiWritePlan.AppendMany(streams, traceId, source)));
        if (TickDecision.IsHeartbeat(hasRoots, hasTrees, hasStreams, hasLeaves))
            // 仅易变叶子 ⇒ 心跳（Tick 判据载体 = 计划动词，非入口自述）。
            return Done(AsTick(UiWritePlan.Leaves(leaves!, interaction, traceId)));

        // 同拍组合（树 → 流 → 叶子）：单一计划、单一写入口。
        var composite = new List<UiWriteTarget>(trees.Count + streams.Count + (leaves?.Count ?? 0));
        composite.AddRange(trees);
        composite.AddRange(streams);
        if (hasLeaves) composite.AddRange(leaves!);
        return Done(RenderOutput.Write(UiWritePlan.Composite(composite, interaction, traceId, source)));
    }

    /// <summary>区域槽键（按视图分区：卸载即失效，切回必回源重写）。</summary>
    private string RegionSlot(string? viewKey, string key)
        => _regionSlotPrefix + (viewKey ?? "-") + "/" + key + AppearanceSuffix();

    /// <summary>流槽键（按 视图 × 流容器 分区；全集指纹水位）。</summary>
    private string StreamSlot(string? viewKey, string containerKey)
        => _regionSlotPrefix + (viewKey ?? "-") + "/stream/" + containerKey + AppearanceSuffix();

    /// <summary>外观指纹后缀（M7）：切肤后旧写槽不复用；空指纹保持旧键形态（零回归）。</summary>
    private string AppearanceSuffix()
        => _appearanceFingerprint?.Invoke() is { Length: > 0 } fingerprint ? "/" + fingerprint : string.Empty;

    /// <summary>写目标名（容器键已是 DOM 名 ⇒ 不重复加前缀）。</summary>
    private string TargetName(string key)
        => key.StartsWith(_regionNamePrefix, StringComparison.Ordinal) ? key : _regionNamePrefix + key;

    /// <summary>稳定输入槽键（按视图分区）。</summary>
    private string StableSlot(string? viewKey) => _stableSlotKey + "/" + (viewKey ?? "-");

    /// <summary>易变输入槽键（按视图分区）。</summary>
    private string VolatileSlot(string? viewKey) => _volatileSlotKey + "/" + (viewKey ?? "-");

    /// <summary>流容器（唯一来源 = 视图程序声明；未声明 ⇒ null，调用方按「有流段」判失败）。</summary>
    private UiViewContainer? StreamContainerFor(string? viewKey)
        => _viewHost.Catalog.TryGet(viewKey, out var blueprint) ? blueprint.StreamContainer : null;

    /// <summary>流全集指纹原料（键 + 内容；顺序敏感 = 流的段序即语义）。</summary>
    private static string StreamSetKey(IReadOnlyList<UiStreamItem> items)
        => string.Join("\u0001", items.Select(static item => item.Key + "\u0002" + item.Html));

    /// <summary>心跳产物唯一产地（Tick 只允许易变叶子）：动词非叶子即结构性违约 ⇒ 显式失败（禁静默降标）。
    /// 判据载体 = 计划动词（结构事实），非入口自述。</summary>
    private static RenderOutput AsTick(UiWritePlan plan)
    {
        if (plan.Kind != UiWriteKind.Leaf)
            throw new InvalidOperationException(
                $"Tick plan allows Leaf verb only (actual={plan.Kind}); region/tree/stream targets are not allowed on Tick path");
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
