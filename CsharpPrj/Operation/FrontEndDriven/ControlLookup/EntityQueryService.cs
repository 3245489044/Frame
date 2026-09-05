
#nullable enable

using System;
using System.Collections.Generic;
using ECS.Core;
using ECS.Components;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlLookup;

/// <summary>
/// 实体查询服务（M6·查找与诊断收编）：按 scope/name/type 查询 ECS 实体，
/// 替代 ControlFinder/ControlTraverser 物理树遍历（命令/诊断侧查找口径收敛到 ECS 真源）。
/// 装配到 AppServiceProvider（PipelineCompositionFactory），命令与诊断经 Resolve 懒解析。
/// </summary>
public sealed class EntityQueryService
{
    private readonly EntityManager _entityManager;

    public EntityQueryService(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        EntityManager = entityManager;
    }

    /// <summary>
    /// 渲染世界（ECS 真源）。本服务以渲染 EntityManager 构造（PipelineCompositionFactory 装配），
    /// 命令/诊断侧经 Resolve&lt;EntityQueryService&gt; 拿到的是渲染世界查询面——
    /// 命令侧自身 EntityManager 为空世界（渲染实体在渲染 ECS），不得直接用于实体遍历。
    /// </summary>
    public EntityManager EntityManager { get; }

    /// <summary>
    /// scope 过滤判定（D5 收敛）：三处查询共用的「scope 非空时须匹配 ScopeComponent」过滤块。
    /// scope 为 null/空串时不过滤（返回 true）；否则须存在 ScopeComponent 且 Scope 相等。
    /// </summary>
    private bool IsInScope(Entity entity, string? scope)
    {
        if (string.IsNullOrEmpty(scope)) return true;
        return _entityManager.TryGetComponent<ScopeComponent>(entity, out var sc)
            && string.Equals(sc.Scope, scope, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按名查实体（可选 scope 过滤）；返回 null 表示未找到。</summary>
    public Entity? FindByName(string name, string? scope = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var entity in _entityManager.GetEntities<ControlInfoComponent>())
        {
            if (!_entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info))
                continue;

            if (!string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsInScope(entity, scope))
                continue;

            return entity;
        }

        return null;
    }

    /// <summary>按类型查实体列表（可选 scope 过滤）。</summary>
    public IReadOnlyList<Entity> QueryByType(string type, string? scope = null)
    {
        var result = new List<Entity>();
        if (string.IsNullOrWhiteSpace(type)) return result;

        foreach (var entity in _entityManager.GetEntities<ControlTypeComponent>())
        {
            if (!_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp))
                continue;

            if (!string.Equals(typeComp.Type, type, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsInScope(entity, scope))
                continue;

            result.Add(entity);
        }

        return result;
    }

    /// <summary>按 scope 查 root 实体（root:{scope}）。</summary>
    public Entity? FindRoot(string scope)
        => FindByName($"root:{scope}", scope);

    /// <summary>
    /// 按 scope 查全部控件实体（D2/D3 收敛入口）：已过滤 SplitterPanel 实体，
    /// scope 为 null/空 时返回全部控件实体。收集（Ports）/可见性检查（RenderVisibilityCheckStage）/
    /// 布局收敛报告（LayoutConvergenceReport）等消费方经此统一过滤，消除各处手写
    /// 「HasComponent&lt;SplitterPanelComponent&gt; + ScopeComponent 匹配」重复。
    /// </summary>
    public IReadOnlyList<Entity> QueryControls(string? scope = null)
    {
        var result = new List<Entity>();
        foreach (var entity in _entityManager.GetEntities<ControlTypeComponent>())
        {
            if (_entityManager.HasComponent<SplitterPanelComponent>(entity))
                continue;

            if (!IsInScope(entity, scope))
                continue;

            result.Add(entity);
        }

        return result;
    }
}
