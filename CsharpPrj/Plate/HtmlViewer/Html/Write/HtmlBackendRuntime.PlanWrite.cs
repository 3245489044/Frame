#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>写计划执行面（从运行时拆出）：区域/键控树/叶子/流/整页五动词 + 预算体量估算入口调用。</summary>
public sealed partial class HtmlBackendRuntime
{
    /// <summary>按区域写回的**不发射**实现（供公开壳/协调器共用；日志由调用壳单点发射）。
    /// <c>Admitted=false</c> ⇒ 未触 DOM（内容未变/版本过期），状态固定 <see cref="DomOpStatus.Ok"/> 仅表示"无需失败处理"。</summary>
    private async Task<(DomOpStatus Status, bool Admitted)> WriteRegionCoreStatusAsync(
        string regionName, string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(regionName)) return (DomOpStatus.Unknown, false);
        var slotKey = RegionSlot(regionName);
        var dedupKey = _fingerprint(html);
        if (!_ledger.TryAdmit(slotKey, dedupKey, out var version))
        {
            _metrics.OnCoalesced();
            return (DomOpStatus.Ok, false);
        }

        var parsed = await ExecuteDomTypedAsync(DomOps.Reconcile, regionName, html, ct).ConfigureAwait(false);
        if (parsed == DomOpStatus.Ok) _ledger.Commit(slotKey, version, dedupKey);
        else _ledger.MarkFailure(slotKey, dedupKey);
        return (parsed, true);
    }

    /// <summary>按区域写回并返回**类型化状态**（供调用方定位失败分支：NotFound=区域容器缺失 / StateNotReady=补丁态模块未就绪）。</summary>
    public async Task<DomOpStatus> WriteRegionStatusAsync(string regionName, string html, CancellationToken ct = default)
    {
        var (parsed, admitted) = await WriteRegionCoreStatusAsync(regionName, html, ct).ConfigureAwait(false);
        if (!admitted)
        {
            Report("EventBridge_writeback", $"区域写回 {regionName} 跳过（仲裁：未变/过期，不触 DOM）");
            return parsed;
        }
        Report("EventBridge_writeback", parsed == DomOpStatus.Ok
            ? $"区域写回 {regionName}: {DomOpStatus.Ok}（不重载文档，UI 位置保持）"
            : $"区域写回 {regionName} 未生效（{parsed}）");
        return parsed;
    }

    /// <summary>流载荷 JSON 口径（camelCase：<c>{items:[{key,html}],cap,direction}</c> / <c>{keep}</c>），与 <c>__uiReconcile.append|trim</c> 契约一致。
    /// 枚举按 camelCase 串落地（<c>newestFirst</c>/<c>oldestFirst</c>），JS 侧据此选择插入/裁剪端。</summary>
    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>流追加唯一实现（不发射轨迹）：按项稳定键定点更新同名项 / 其余插入流首 / cap 裁尾，不重建文档。</summary>
    private async Task<(DomOpStatus Status, bool Admitted)> WriteStreamCoreStatusAsync(
        string streamName, UiStreamAppend payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(streamName) || payload is null) return (DomOpStatus.Unknown, false);
        var slotKey = StreamSlot(streamName);
        var json = JsonSerializer.Serialize(payload, StreamJsonOptions);
        var dedupKey = _fingerprint("append|" + json);
        if (!_ledger.TryAdmit(slotKey, dedupKey, out var version))
        {
            _metrics.OnCoalesced();
            return (DomOpStatus.Ok, false);
        }

        var parsed = await ExecuteDomTypedAsync(DomOps.Append, streamName, json, ct).ConfigureAwait(false);
        if (parsed == DomOpStatus.Ok) _ledger.Commit(slotKey, version, dedupKey);
        else _ledger.MarkFailure(slotKey, dedupKey);
        return (parsed, true);
    }

    /// <summary>流裁剪唯一实现（不发射轨迹）：保留流首 N 项。</summary>
    private async Task<(DomOpStatus Status, bool Admitted)> WriteTrimCoreStatusAsync(
        string streamName, UiStreamTrim payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(streamName) || payload is null) return (DomOpStatus.Unknown, false);
        var slotKey = StreamSlot(streamName);
        var json = JsonSerializer.Serialize(payload, StreamJsonOptions);
        var dedupKey = _fingerprint("trim|" + json);
        if (!_ledger.TryAdmit(slotKey, dedupKey, out var version))
        {
            _metrics.OnCoalesced();
            return (DomOpStatus.Ok, false);
        }

        var parsed = await ExecuteDomTypedAsync(DomOps.Trim, streamName, json, ct).ConfigureAwait(false);
        if (parsed == DomOpStatus.Ok) _ledger.Commit(slotKey, version, dedupKey);
        else _ledger.MarkFailure(slotKey, dedupKey);
        return (parsed, true);
    }

    /// <summary>流追加公开壳（append-only 面增量落地）：<paramref name="items"/> 按「旧→新」序给出，
    /// <paramref name="cap"/> 为流长度上限，<paramref name="direction"/> 决定插入/裁剪端。
    /// 返回 false = 未命中 / 流原语未就绪（STATE_NOT_READY）/ 被仲裁去重。</summary>
    public async Task<bool> WriteStreamAsync(
        string streamName, IReadOnlyList<UiStreamItem> items, int? cap = null,
        UiStreamDirection direction = UiStreamDirection.NewestFirst, CancellationToken ct = default)
    {
        if (items is null || items.Count == 0) return false;
        var (status, admitted) = await WriteStreamCoreStatusAsync(streamName, new UiStreamAppend(items, cap, direction), ct).ConfigureAwait(false);
        Report("EventBridge_writeback", admitted
            ? $"流追加 {streamName}: {status}（items={items.Count}, cap={(cap?.ToString() ?? "-")}, direction={direction}）"
            : $"流追加 {streamName} 跳过（仲裁：未变/过期，不触 DOM）");
        return admitted && status == DomOpStatus.Ok;
    }

    /// <summary>按叶子节点定点写回（S5/L3）：经受控 <c>DomOps.Set</c> 仅更新命中的单元素文本（如 elapsed 徽章），
    /// 不触发区域重渲染；用于心跳刷新只改易变叶子的场景（DOM 变更计数=1）。name 命中失败返回 false。
    /// <para><see cref="DomOps.Set"/> 语义：表单元素写 value，<c>.host</c> 写 innerHTML，其余写 textContent。</para></summary>
    public async Task<bool> WriteLeafAsync(string name, string value, CancellationToken ct = default)
    {
        var status = await WriteLeafCoreStatusAsync(name, value, ct).ConfigureAwait(false);
        var ok = status == DomOpStatus.Ok;
        Report("EventBridge_writeback", ok
            ? $"叶子写回 {name}: {DomOpStatus.Ok}"
            : $"叶子写回 {name} 未命中（{status}）");
        return ok;
    }

    /// <summary>叶子写入唯一实现（不发射轨迹）：供公开壳与协调器共用，叶子日志单点发射（L-02）。</summary>
    private async Task<DomOpStatus> WriteLeafCoreStatusAsync(string name, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return DomOpStatus.Unknown;
        return await ExecuteDomTypedAsync(DomOps.Set, name, value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 单一写回协调器入口（L2 / R3）：接收 <see cref="UiWritePlan"/> 独占执行区域/键控树/叶子/整文档写回，
    /// 逐目标独立判成败、产出 <see cref="UiWriteResult"/>（类型化显式策略，不再靠哨兵）。
    /// <para>Region/Leaf 目标名 = 最终 DOM 名（如 <c>region-aside</c>）；Region 默认经键控对齐原语落地。
    /// FullDocument 经 <see cref="HtmlWritePump"/> 收敛写或纯渲染（无宿主端口 = 显式 Failed，不静默吞）。</para>
    /// </summary>
    public Task<UiWriteResult> WriteAsync(UiWritePlan plan, CancellationToken ct = default)
        => WriteAsync(plan, plan?.Source ?? UiWriteSource.Mount, null, ct);

    /// <summary>单一写回协调器入口（带刷新追踪号：一次刷新一条轨迹，<paramref name="traceId"/> 由刷新链路传入）。
    /// 来源缺省取计划自述（<see cref="UiWritePlan.Source"/>）——B4 起调用方按入口标注。</summary>
    public Task<UiWriteResult> WriteAsync(UiWritePlan plan, string? traceId, CancellationToken ct = default)
        => WriteAsync(plan, plan?.Source ?? UiWriteSource.Mount, traceId, ct);

    /// <summary>单一写回协调器入口（来源显式）：来源决定收敛窗口/合并优先级，并写入轨迹（可复算归因）。
    /// 本层同时承担：交互快路径开关归一、帧预算降级、指标埋点与 60s 限流上报。</summary>
    /// <summary>把页面输入态并入写回计划（键控对齐后回放输入值，防 textarea 重建丢草稿）。</summary>
    private UiWritePlan? MergeInputState(UiWritePlan? plan)
    {
        if (plan is null) return null;
        var inputs = PageState.Inputs;
        if (inputs.Count == 0) return plan;

        var current = plan.Interaction ?? UiInteractionSnapshot.Empty;
        return plan with { Interaction = current with { Inputs = inputs } };
    }

    public async Task<UiWriteResult> WriteAsync(UiWritePlan plan, UiWriteSource source, string? traceId = null, CancellationToken ct = default)
    {
        // 交互快路径开关（关 ⇒ 交互按刷新语义；单开关回退，行为等价 B3 结束态）。
        if (!_options.InteractiveFastPath && source == UiWriteSource.Interactive)
            source = UiWriteSource.Refresh;

        var traceFromPlan = !string.IsNullOrWhiteSpace(plan?.TraceId);
        var trace = plan?.TraceId;
        if (string.IsNullOrWhiteSpace(trace))
            trace = string.IsNullOrWhiteSpace(traceId) ? _traceFactory("write") : traceId;
        var traceSource = traceFromPlan ? "plan" : string.IsNullOrWhiteSpace(traceId) ? "factory" : "caller";
        if (traceSource == "factory")
            Report("EventBridge_writeback",
                $"写回追踪号自造 trace={trace} kind={plan?.Kind} source={source}（调用链未传 traceId；刷新驱动写应在上层携带）");

        plan = MergeInputState(plan);
        _metrics.OnPlan(plan?.Targets.Count ?? 0);
        var effective = ApplyFrameBudget(plan, trace!);

        // 封送排队探针（单源）：先清峰值，再按本轨迹窗口取峰值（区分「UI 线程被占」与「V8/DOM 执行慢」）。
        UiThreadMarshaler.TakePeakQueueWaitMs();
        var started = Environment.TickCount64;
        // 节批量写入口（S2，灰度开关）：一拍一 batch、一次跨进程；关 ⇒ 旧逐目标路径（行为等价）。
        var result = _options.UseSectionWriter
            ? await SubmitAsync(SectionPlanAdapter.From(effective), source, trace, ct).ConfigureAwait(false)
            : await WriteCoreAsync(effective, trace!, ct).ConfigureAwait(false);
        var elapsed = Environment.TickCount64 - started;
        var marshalWait = UiThreadMarshaler.TakePeakQueueWaitMs();
        _metrics.OnMarshalWait(marshalWait);
        // 指标只统计"真实落地"：未变/失败/空计划不计入耗时与整页渲染（防 P50/P95 被空写稀释）。
        var applied = result.Status is UiWriteStatus.Applied or UiWriteStatus.Partial;
        if (applied) _metrics.OnApplied(elapsed);
        if (effective.Kind == UiWriteKind.FullDocument && applied)
            _metrics.OnRender(EstimateBytes(effective.Targets));
        if (_options.FrameBudget && elapsed > _options.MaxMsPerFlush)
            _metrics.OnDegrade("slow-flush");

        // L-01/L-02：一次写回只产一条轨迹——Tree/Leaf/TreeAndLeaf 子写只回结构，汇总在本层唯一发射。
        Report("EventBridge_writeback",
            $"写回协调器 {plan?.Kind} source={source} planSource={plan?.Source} trace={trace} sourceFrom={traceSource}: {result.Status}, written=[{string.Join(",", result.Written)}], " +
            $"unchanged=[{string.Join(",", result.UnchangedTargets ?? Array.Empty<string>())}], failed=[{string.Join(",", result.FailedTargets ?? Array.Empty<string>())}], " +
            $"elapsedMs={elapsed}, marshalWaitMs={marshalWait}{(result.Anomalies is { Count: > 0 } ? ", anomalies=1" : string.Empty)}");
        ReportMetricsIfDue();
        return result;
    }

    /// <summary>帧预算（B5-3 降级链）：裁决单源 = <see cref="WriteBudgetPolicy.DecideTargets"/>；本层只负责记账与可见化。</summary>
    private UiWritePlan ApplyFrameBudget(UiWritePlan? plan, string trace)
    {
        if (!_options.FrameBudget || plan is null || plan.Kind == UiWriteKind.Skip) return plan!;

        var decision = WriteBudgetPolicy.DecideTargets(
            plan, _options.MaxOpsPerFlush, _options.MaxBytesPerFlush, EstimateBytes, EstimateTargetBytes);

        if (decision.Kept.Count == 0)
        {
            _metrics.OnDropped(decision.DegradeReason ?? "over-budget", plan.Targets.Count);
            Report("EventBridge_writeback",
                $"帧预算降级：整计划丢弃 kind={plan.Kind} ops={plan.Targets.Count} reason={decision.DegradeReason} trace={trace}");
            return UiWritePlan.Skip;
        }

        if (decision.Dropped > 0)
            _metrics.OnDropped(decision.DegradeReason ?? "over-budget", decision.Dropped);

        // 单目标超限：整区/整页写是该区域的唯一刷新手段，丢弃只会留下陈旧 DOM（比慢更差）⇒ 保留并可见化。
        if (decision.OverCapAfterKeep)
        {
            _metrics.OnDegrade("oversize-single-target");
            Report("EventBridge_writeback",
                $"帧预算超限（保留单目标落地，不丢弃）：kind={plan.Kind} kept={decision.Kept.Count} " +
                $"capOps={_options.MaxOpsPerFlush} capBytes={_options.MaxBytesPerFlush} trace={trace}");
            return plan with { Targets = decision.Kept };
        }

        if (decision.Dropped > 0)
            Report("EventBridge_writeback",
                $"帧预算降级：ops {plan.Targets.Count}→{decision.Kept.Count}（dropped={decision.Dropped}, reason={decision.DegradeReason}）trace={trace}");
        return plan with { Targets = decision.Kept };
    }

    /// <summary>单目标体量（字节；树按结构递归估算，整页/区域按正文长度）。</summary>
    private static int EstimateTargetBytes(UiWriteTarget target)
    {
        var bytes = target.Name?.Length ?? 0;
        return bytes + (target.Payload switch
        {
            string text => text.Length,
            UiStreamAppend append => append.Items.Sum(item => item.Key.Length + item.Html.Length),
            UiStreamTrim => 16,
            UiNode tree => EstimateNodeBytes(tree),
            _ => 0,
        });
    }

    /// <summary>计划体量估算（ops 之外的字节口径；树按结构递归估算——整树下发是该面的主要耗时来源）。</summary>
    private static int EstimateBytes(IReadOnlyList<UiWriteTarget> targets)
    {
        var bytes = 0;
        foreach (var target in targets) bytes += EstimateTargetBytes(target);
        return bytes;
    }

    /// <summary>键控树体量估算（text/attrs/key/tag 近似；用于预算判据，不追求精确序列化长度）。</summary>
    private static int EstimateNodeBytes(UiNode node)
    {
        var bytes = 16 + (node.Tag?.Length ?? 0) + (node.Key?.Length ?? 0) + (node.Text?.Length ?? 0);
        if (node.Attributes is { Count: > 0 })
        {
            foreach (var kv in node.Attributes) bytes += 4 + kv.Key.Length + (kv.Value?.Length ?? 0);
        }

        if (node.Children is { Count: > 0 })
        {
            foreach (var child in node.Children) bytes += EstimateNodeBytes(child);
        }

        return bytes;
    }

    /// <summary>指标上报（60s 限流 + 单发：防指标自身洪泛）。</summary>
    private void ReportMetricsIfDue()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastMetricsReportMs);
        if (now - last < _options.MetricsReportIntervalMs) return;
        if (Interlocked.CompareExchange(ref _lastMetricsReportMs, now, last) != last) return;
        Report("UiWriteMetrics", _metrics.Snapshot());
    }

    /// <summary>写回核心（唯一结构实现）：子写递归本方法且不发射汇总；轨迹由公开 <see cref="WriteAsync(UiWritePlan, UiWriteSource, string?, CancellationToken)"/> 单点发射。</summary>
    private async Task<UiWriteResult> WriteCoreAsync(UiWritePlan? plan, string trace, CancellationToken ct)
    {
        if (plan is null || plan.Kind == UiWriteKind.Skip)
            return new UiWriteResult(UiWriteStatus.Nothing, Array.Empty<string>(), Array.Empty<string>());
        var written = new List<string>();
        var unchanged = new List<string>();
        var failed = new List<string>();
        var anomalies = new List<string>();

        switch (plan.Kind)
        {
            case UiWriteKind.Region:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not string regionHtml)
                    {
                        Report("EventBridge_writeback",
                            $"区域写回 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 string；null 不等于空串清空）trace={trace}");
                        failed.Add(name);
                        continue;
                    }
                    var (regionStatus, regionAdmitted) = await WriteRegionCoreStatusAsync(name, regionHtml, ct).ConfigureAwait(false);
                    if (!regionAdmitted) unchanged.Add(name);
                    else if (regionStatus == DomOpStatus.Ok) written.Add(name);
                    else failed.Add(name);
                }
                break;

            case UiWriteKind.Append:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not UiStreamAppend append)
                    {
                        Report("EventBridge_writeback",
                            $"流追加 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 UiStreamAppend）trace={trace}");
                        failed.Add(name);
                        continue;
                    }
                    var (appendStatus, appendAdmitted) = await WriteStreamCoreStatusAsync(name, append, ct).ConfigureAwait(false);
                    if (!appendAdmitted) unchanged.Add(name);
                    else if (appendStatus == DomOpStatus.Ok) written.Add(name);
                    else failed.Add(name);
                }
                break;

            case UiWriteKind.Trim:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not UiStreamTrim trim)
                    {
                        Report("EventBridge_writeback",
                            $"流裁剪 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 UiStreamTrim）trace={trace}");
                        failed.Add(name);
                        continue;
                    }
                    var (trimStatus, trimAdmitted) = await WriteTrimCoreStatusAsync(name, trim, ct).ConfigureAwait(false);
                    if (!trimAdmitted) unchanged.Add(name);
                    else if (trimStatus == DomOpStatus.Ok) written.Add(name);
                    else failed.Add(name);
                }
                break;

            case UiWriteKind.Tree:
                // 计划指纹只对单目标有效：多目标共用同一指纹会让后续目标全部被判"未变"（写丢失）。
                var sharedFingerprint = plan.Targets.Count == 1 ? plan.Fingerprint : null;
                if (plan.Targets.Count > 1 && !string.IsNullOrEmpty(plan.Fingerprint))
                    Report("EventBridge_writeback",
                        $"键控树写回忽略计划指纹（多目标共用会误判未变；targets={plan.Targets.Count}）trace={trace}");
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not UiNode tree)
                    {
                        // 载荷类型不符可见化（治 E-6 静默）：本应恒为 UiNode，不符即产/传链漂移，显式上报而非静默 skip。
                        Report("EventBridge_writeback",
                            $"键控树写回 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 UiNode）trace={trace}");
                        failed.Add(name);
                        continue;
                    }
                    // D-1 去重（与 Region 路径语义一致）：同目标 + 同树/同交互指纹 ⇒ 不触 DOM（Unchanged，非失败）。
                    var treeFp = sharedFingerprint ?? HtmlTreeWriter.Fingerprint(tree, plan.Interaction);
                    var treeSlotKey = TreeSlot(name);
                    if (!_ledger.TryAdmit(treeSlotKey, treeFp, out var treeVersion))
                    {
                        _metrics.OnCoalesced();
                        Report("EventBridge_writeback", $"键控树写回 {name} 跳过（仲裁：未变/过期）trace={trace}");
                        unchanged.Add(name);
                        continue;
                    }
                    var r = await _treeWriter.ReconcileAsync(name, tree, plan.Interaction, ct).ConfigureAwait(false);
                    // 键控对齐异常（重复键等）显式上报（I3：不静默；对齐结果类型化回传）。
                    if (r.Anomalies is { Count: > 0 })
                    {
                        anomalies.AddRange(r.Anomalies);
                        Report("Reconcile_anomaly",
                            $"键控对齐异常 target={name} trace={trace}: {string.Join(";", r.Anomalies)}");
                    }
                    // E-5：失败原因类型化可见化（区域缺失 vs 内核未就绪）——供上层定向自愈，不静默。
                    if (r.Status is UiWriteStatus.NotFound or UiWriteStatus.NotReady)
                        Report("EventBridge_writeback",
                            $"键控树写回 {name} 未落地（{r.Status}）trace={trace}");
                    // X-5：anomalies 非空 ⇒ 失败/部分（不计入 written），判据不合格不放行。
                    if ((r.Status is UiWriteStatus.Applied or UiWriteStatus.Partial) && r.Anomalies is not { Count: > 0 })
                    {
                        _ledger.Commit(treeSlotKey, treeVersion, treeFp);
                        written.Add(name);
                    }
                    else
                    {
                        _ledger.MarkFailure(treeSlotKey, treeFp);
                        failed.Add(name);
                    }
                }
                break;

            case UiWriteKind.TreeAndLeaf:
                // 同拍两通道：先树（区域重建），后叶子（定点值更新，命中重建后的 DOM 元素）。
                var treeResult = await WriteCoreAsync(UiWritePlan.TreesTraced(
                        FilterTargets(plan.Targets, static t => t.Payload is UiNode), plan.Interaction, trace),
                    trace, ct).ConfigureAwait(false);
                var leafResult = await WriteCoreAsync(UiWritePlan.Leaves(
                        FilterTargets(plan.Targets, static t => t.Payload is string), plan.Interaction, trace),
                    trace, ct).ConfigureAwait(false);
                written.AddRange(treeResult.Written);
                written.AddRange(leafResult.Written);
                unchanged.AddRange(treeResult.UnchangedTargets ?? Array.Empty<string>());
                unchanged.AddRange(leafResult.UnchangedTargets ?? Array.Empty<string>());
                failed.AddRange(treeResult.FailedTargets ?? Array.Empty<string>());
                failed.AddRange(leafResult.FailedTargets ?? Array.Empty<string>());
                if (treeResult.Anomalies is { Count: > 0 })
                {
                    anomalies.AddRange(treeResult.Anomalies);
                }
                break;

            case UiWriteKind.Leaf:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not string value)
                    {
                        // null/非串载荷不得被压平为 "" 清空目标（静默抹值）。
                        Report("EventBridge_writeback",
                            $"叶子写回 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 string；null ≠ 清空）trace={trace}");
                        failed.Add(name);
                        continue;
                    }
                    if (await WriteLeafCoreStatusAsync(name, value, ct).ConfigureAwait(false) == DomOpStatus.Ok) written.Add(name);
                    else failed.Add(name);
                }
                break;

            case UiWriteKind.Composite:
                // 同拍组合（顺序 = 区域 → 树 → 流 → 叶子）：按载荷形态分派；无法分派的载荷必须显式失败（禁静默丢写）。
                var compositeRegions = FilterTargets(plan.Targets, static t => t.Payload is UiRegionHtml);
                if (compositeRegions.Count > 0)
                {
                    var regionTargets = new List<UiWriteTarget>(compositeRegions.Count);
                    foreach (var target in compositeRegions)
                    {
                        regionTargets.Add(new UiWriteTarget(target.Name, ((UiRegionHtml)target.Payload!).Html));
                    }

                    var regionPart = await WriteCoreAsync(
                        new UiWritePlan(UiWriteKind.Region, regionTargets, TraceId: trace, Source: plan.Source), trace, ct).ConfigureAwait(false);
                    written.AddRange(regionPart.Written);
                    unchanged.AddRange(regionPart.UnchangedTargets ?? Array.Empty<string>());
                    failed.AddRange(regionPart.FailedTargets ?? Array.Empty<string>());
                }

                var compositeTrees = FilterTargets(plan.Targets, static t => t.Payload is UiNode);
                if (compositeTrees.Count > 0)
                {
                    var treePart = await WriteCoreAsync(
                        UiWritePlan.TreesTraced(compositeTrees, plan.Interaction, trace), trace, ct).ConfigureAwait(false);
                    written.AddRange(treePart.Written);
                    unchanged.AddRange(treePart.UnchangedTargets ?? Array.Empty<string>());
                    failed.AddRange(treePart.FailedTargets ?? Array.Empty<string>());
                    if (treePart.Anomalies is { Count: > 0 })
                    {
                        anomalies.AddRange(treePart.Anomalies);
                    }
                }

                var compositeStreams = FilterTargets(plan.Targets, static t => t.Payload is UiStreamAppend);
                if (compositeStreams.Count > 0)
                {
                    var streamPart = await WriteCoreAsync(
                        UiWritePlan.AppendMany(compositeStreams, trace), trace, ct).ConfigureAwait(false);
                    written.AddRange(streamPart.Written);
                    unchanged.AddRange(streamPart.UnchangedTargets ?? Array.Empty<string>());
                    failed.AddRange(streamPart.FailedTargets ?? Array.Empty<string>());
                }

                var compositeTrims = FilterTargets(plan.Targets, static t => t.Payload is UiStreamTrim);
                if (compositeTrims.Count > 0)
                {
                    var trimPart = await WriteCoreAsync(
                        new UiWritePlan(UiWriteKind.Trim, compositeTrims, TraceId: trace), trace, ct).ConfigureAwait(false);
                    written.AddRange(trimPart.Written);
                    unchanged.AddRange(trimPart.UnchangedTargets ?? Array.Empty<string>());
                    failed.AddRange(trimPart.FailedTargets ?? Array.Empty<string>());
                }

                var compositeLeaves = FilterTargets(plan.Targets, static t => t.Payload is string);
                if (compositeLeaves.Count > 0)
                {
                    var leafPart = await WriteCoreAsync(
                        UiWritePlan.Leaves(compositeLeaves, plan.Interaction, trace), trace, ct).ConfigureAwait(false);
                    written.AddRange(leafPart.Written);
                    unchanged.AddRange(leafPart.UnchangedTargets ?? Array.Empty<string>());
                    failed.AddRange(leafPart.FailedTargets ?? Array.Empty<string>());
                }

                // 未知形态（null / 未登记的载荷类型）不得被静默吞掉：逐目标显式失败 + 诊断。
                foreach (var t in plan.Targets)
                {
                    if (t.Payload is UiNode or UiStreamAppend or UiStreamTrim or UiRegionHtml or string) continue;
                    var name = t.Name ?? string.Empty;
                    Report("EventBridge_writeback",
                        $"同拍组合 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（树/流追加/流裁剪/叶子四类之外）trace={trace}");
                    failed.Add(name);
                }
                break;

            case UiWriteKind.FullDocument:
                var html = plan.Targets.Count > 0 ? plan.Targets[0].Payload as string : null;
                if (html is null)
                {
                    Report("EventBridge_writeback", $"整页写回跳过：载荷非字符串（{plan.Targets.Count} 目标）trace={trace}");
                    return new UiWriteResult(UiWriteStatus.Failed, written, new[] { "body" }, Fallback: "navigate", TraceId: trace,
                        FailedTargets: new[] { "body" });
                }
                var status = await WriteFullPageAsync(html, ct).ConfigureAwait(false);
                if (status == HtmlWriteBackStatus.Failed)
                {
                    return new UiWriteResult(UiWriteStatus.Failed, written, new[] { "body" }, Fallback: "navigate", TraceId: trace,
                        FailedTargets: new[] { "body" });
                }
                written.Add("body");
                break;
        }

        // 结果代数单一裁决：Partial ⇔ (failed>0 ∨ anomalies≠∅) ∧ applied>0；未变不产生 Partial。
        return WriteOutcome
            .Classify(written, unchanged, failed, anomalies.Count > 0 ? anomalies : null)
            .ToResult(trace);
    }

    /// <summary>桥模块就绪查询（L1 <see cref="IUiBridgeReadiness"/>；通道未实现 → 返回 None，调用方据显式状态处置）。</summary>
    public async Task<UiBridgeModuleState> GetBridgeModulesAsync(CancellationToken ct = default)
        => _channel is IUiBridgeReadiness r
            ? await r.GetBridgeModulesAsync(ct).ConfigureAwait(false)
            : UiBridgeModuleState.None;

    /// <summary>合并计划的目标分组（按载荷形态区分树/叶子；保持原序）。</summary>
    private static IReadOnlyList<UiWriteTarget> FilterTargets(
        IReadOnlyList<UiWriteTarget> targets, Func<UiWriteTarget, bool> predicate)
    {
        var list = new List<UiWriteTarget>(targets.Count);
        foreach (var target in targets)
        {
            if (predicate(target)) list.Add(target);
        }

        return list;
    }

    /// <summary>整页写（显式策略）：有收敛 pump → 收敛写（Committed/Degraded=成功，Failed=失败）；否则纯渲染。</summary>
    private async Task<HtmlWriteBackStatus> WriteFullPageAsync(string html, CancellationToken ct)
    {
        if (_writePump is not null)
        {
            // 整页写 = 首挂载语义（窗口最宽）；来源显式标注以免被刷新窗口误伤。
            var result = await _writePump(html, UiWriteSource.Mount, ct).ConfigureAwait(false);
            return result.Status;
        }
        await _channel.RenderHtmlAsync(html, ct).ConfigureAwait(false);
        return HtmlWriteBackStatus.Committed;
    }
}
