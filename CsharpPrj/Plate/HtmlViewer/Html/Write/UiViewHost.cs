#nullable enable

using System;
using Modules.DeclarativeDataflow.Lease;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>视图切换决策（框架输出）：<see cref="Changed"/>=false ⇒ Keep（不产根投影）。</summary>
public readonly record struct UiViewTransition(string? From, string To, bool Changed, string? RootName, string? RootHtml)
{
    /// <summary>保持当前挂载（无切换）。</summary>
    public static UiViewTransition Keep(string? viewKey)
        => new(viewKey, viewKey ?? string.Empty, false, null, null);
}

/// <summary>
/// 视图宿主（框架件，唯一挂载/卸载/切换裁决者）——持有「当前已挂载视图」，产出根子集投影。
/// <para>卸载不由写通道的“删除/清空”表达：根投影只含目标视图根（键 <c>view:{viewKey}</c>），
/// 缺失的旧根由通用键控对齐原语删除 ⇒ 生命周期逻辑全部在本类，DOM 侧零视图知识。</para>
/// <para>原子性（VSC-2）：决策与渲染失败 ⇒ 不推进当前视图、不产计划（旧根保留）。</para>
/// <para>状态机单源 = <see cref="LeaseMachine{T}"/>（Modules，载荷无关）：
/// <see cref="Ensure"/> ⇒ Mount，<see cref="Confirm"/> ⇒ Confirm，失败 ⇒ Reject（当前租约不推进）。
/// 本类只提供「视图程序查表 + 骨架兜底 + 容器存在性断言」这些宿主事实。</para>
/// </summary>
public sealed class UiViewHost
{
    /// <summary>已挂载视图（键 + 根片段）——租约载荷（本类私有，不外泄到 Modules）。</summary>
    private sealed record MountedView(string Key, string Html);

    private readonly UiViewBlueprintCatalog _catalog;
    private readonly Action<string>? _onDiagnostic;
    private readonly LeaseMachine<MountedView> _lease = new();

    public UiViewHost(UiViewBlueprintCatalog catalog, Action<string>? onDiagnostic = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _onDiagnostic = onDiagnostic;
    }

    /// <summary>当前已挂载视图（未挂载 ⇒ null）。</summary>
    public string? CurrentView => _lease.Current?.Key;

    /// <summary>挂载租约相位（诊断/断言；运行态词一律经 <see cref="LeasePhaseStages.Map"/>）。</summary>
    public LeasePhase Phase => _lease.Phase;

    /// <summary>视图程序目录（只读）。</summary>
    public UiViewBlueprintCatalog Catalog => _catalog;

    /// <summary>
    /// 确保目标视图已挂载：未声明 ⇒ Keep + 诊断；同视图 ⇒ Keep；切换 ⇒ 产根投影（<paramref name="renderRoot"/> 失败则 Keep）。
    /// </summary>
    /// <param name="viewKey">目标视图键（null/空白 ⇒ Keep，无视图语义）。</param>
    /// <param name="renderRoot">视图根渲染委托（返回视图根片段；null ⇒ 用声明骨架兜底）。</param>
    public UiViewTransition Ensure(string? viewKey, Func<string, string>? renderRoot = null)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) return UiViewTransition.Keep(CurrentView);

        var key = viewKey!.Trim();
        if (!_catalog.TryGet(key, out var blueprint))
        {
            _onDiagnostic?.Invoke("view-program-missing:" + key);
            return UiViewTransition.Keep(CurrentView);
        }

        if (_lease.Phase == LeasePhase.Open
            && string.Equals(_lease.Current?.Key, key, StringComparison.Ordinal))
            return UiViewTransition.Keep(CurrentView);

        string rootHtml;
        try
        {
            rootHtml = renderRoot?.Invoke(key) ?? string.Empty;
        }
        catch (Exception ex)
        {
            // 装配失败 ⇒ 保留旧根（不推进当前视图），由调用方可见化（VSC-2）。
            Reject(key, "view_mount_failed:" + key + ":" + ex.GetType().Name);
            return UiViewTransition.Keep(CurrentView);
        }

        if (string.IsNullOrWhiteSpace(rootHtml)) rootHtml = UiViewSkeleton.Build(blueprint);
        else if (!UiViewSkeleton.ContainsAllContainers(rootHtml, blueprint))
        {
            // 渲染产物缺声明容器 ⇒ 判据不合格：不切换（否则写目标会落空/写孤儿）。
            Reject(key, "view_root_incomplete:" + key);
            return UiViewTransition.Keep(CurrentView);
        }

        var from = CurrentView;
        if (!_lease.Mount(new MountedView(key, rootHtml))) return UiViewTransition.Keep(CurrentView);

        return new UiViewTransition(from, key, true, _catalog.RootName, rootHtml);
    }

    /// <summary>提交切换（计划已被写协调器采纳）：仅在根投影真正进入写计划后调用，失败拍不提交 ⇒ 下一拍重试。</summary>
    public void Confirm(string? viewKey)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) return;
        if (_lease.Phase != LeasePhase.Mounted) return;
        if (!string.Equals(_lease.Pending?.Key, viewKey!.Trim(), StringComparison.Ordinal)) return;
        _lease.Confirm();
    }

    /// <summary>拒绝待确认挂载（渲染/断言失败路径）：当前租约不推进，原因显式可见。</summary>
    /// <param name="viewKey">待确认视图键；与待确认项不符 ⇒ 不改状态。</param>
    /// <param name="reason">诊断原因（null ⇒ 只改状态不产诊断）。</param>
    public void Reject(string? viewKey, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) return;
        if (!string.Equals(_lease.Pending?.Key, viewKey!.Trim(), StringComparison.Ordinal)) return;
        if (!_lease.Reject(reason)) return;
        if (!string.IsNullOrEmpty(reason)) _onDiagnostic?.Invoke(reason);
    }

    /// <summary>释放当前挂载（按策略：OnDemand ⇒ 释放；Persistent ⇒ 保留）。</summary>
    /// <returns>true = 已释放；false = 未释放（无挂载或常驻策略）。</returns>
    public bool Release(LeasePolicy policy = LeasePolicy.OnDemand) => _lease.Release(policy);
}

