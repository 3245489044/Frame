#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS.Components;

namespace FrontEndDriven.ECS.Systems;

/// <summary>
/// Tab 内容重排触发系统（重构后角色）：TabControl 被 WinForms 布局引擎更新尺寸后其自身
/// Resize 触发，选中页变化时 SelectedIndexChanged 触发——这两者都是「Tab 可达成显示区
/// 就绪/可能变化」的真源信号（页签增删、multiline 换行改变页签条高度时 inset 变化）。
/// 本系统只把这些信号转译成一次该 scope 的 reflow 调度（RequestReflow），
/// 不再向任何缓存组件写几何。基准由 ContainerBasisResolver 在 reflow 内同步推导。
/// 幂等：同实体不重复订阅。销毁路径 Detach 单一收口。
/// </summary>
public sealed class TabContentReflowSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    private readonly ConcurrentDictionary<string, List<AttachedTab>> _attached = new(StringComparer.OrdinalIgnoreCase);

    private sealed record AttachedTab(Entity Entity, TabControl Control, EventHandler ResizeHandler, EventHandler SelectedHandler);

    public TabContentReflowSystem(EntityManager entityManager, ControlPool controlPool)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    public void Attach(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;

        var list = _attached.GetOrAdd(scope, _ => new List<AttachedTab>());
        foreach (var entity in _entityManager.GetEntities<TabContainerComponent>())
        {
            if (!LayoutQuery.IsInScope(_entityManager, entity, scope)) continue;
            if (list.Any(t => t.Entity == entity)) continue;
            if (_controlPool.GetControl(entity) is not TabControl tc || tc.IsDisposed) continue;

            var resizeHandler = new EventHandler((_, _) => OnTabContentChanged(scope, entity, tc));
            var selectedHandler = new EventHandler((_, _) => OnTabContentChanged(scope, entity, tc));
            tc.Resize += resizeHandler;
            tc.SelectedIndexChanged += selectedHandler;
            list.Add(new AttachedTab(entity, tc, resizeHandler, selectedHandler));
        }
    }

    public void Detach(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        if (!_attached.TryRemove(scope, out var list)) return;
        foreach (var tab in list)
        {
            try
            {
                tab.Control.Resize -= tab.ResizeHandler;
                tab.Control.SelectedIndexChanged -= tab.SelectedHandler;
            }
            catch { /* 控件已释放，退订可忽略 */ }
        }
    }

    private void OnTabContentChanged(string scope, Entity entity, TabControl tc)
    {
        if (tc.IsDisposed || tc.Disposing) { DetachOne(scope, entity); return; }

        // 真源已就绪（TabControl 布局完成 / 选中页变化）→ 触发该 scope 一次 reflow；
        // 基准由 ContainerBasisResolver 在 reflow 内同步读取 DisplayRectangle 偏移量，无需写缓存。
        AppServiceProvider.Resolve<LayoutSubsystem>()?.RequestReflow(scope);
    }

    /// <summary>仅退订并移除指定实体的订阅——避免一个 tab 释放时误退订本 scope 全部 tab（一损俱损）。</summary>
    private void DetachOne(string scope, Entity entity)
    {
        if (!_attached.TryGetValue(scope, out var list)) return;
        var hit = list.FirstOrDefault(t => t.Entity == entity);
        if (hit == null) return;
        try { hit.Control.Resize -= hit.ResizeHandler; } catch { }
        try { hit.Control.SelectedIndexChanged -= hit.SelectedHandler; } catch { }
        list.Remove(hit);
        if (list.Count == 0) _attached.TryRemove(scope, out _);
    }
}