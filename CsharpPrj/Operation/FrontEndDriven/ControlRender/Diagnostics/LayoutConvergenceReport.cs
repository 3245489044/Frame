
#nullable enable

using System;
using System.Collections.Generic;
using ECS.Core;
using ECS.Components;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;

namespace FrontEndDriven.ControlRender.Diagnostics;

/// <summary>布局收敛报告结果（M1·结果契约）。</summary>
public sealed record LayoutConvergenceResult(
    int Total,
    int Valid,
    int ZeroSized,
    int Missing,
    IReadOnlyList<string> Problems)
{
    /// <summary>收敛判据：无 0 尺寸、无缺失布局实体。</summary>
    public bool IsConverged => ZeroSized == 0 && Missing == 0;
}

/// <summary>
/// 布局收敛报告（M1·结果契约）：每次布局计算后断言「全部实体已取得 ComputedLayout 且尺寸>0」。
/// 缺失/0 尺寸实体显式 Warning（Info 常开）——「控件在树但屏幕不可见」的 ECS 侧可观测基线，
/// 与物理侧只读审计（SyncActualContainerSizes/AuditContainerSizeConsistency）构成双向对照。
/// </summary>
public static class LayoutConvergenceReport
{
    public static LayoutConvergenceResult Report(EntityManager entityManager, string? scope = null)
    {
        var problems = new List<string>();
        int total = 0, valid = 0, zeroSized = 0, missing = 0;

        // 布局收敛判据口径：仅断言「有布局计算义务」的实体（含 ProportionalLayoutComponent），
        // 排除宿主注入类容器（如 type=container 的 EcsConfigContainer，不参与 ECS 比例布局）
        foreach (var entity in entityManager.GetEntities<ProportionalLayoutComponent>())
        {
            // SplitterPanel 实体由 SplitContainerSystem 管理，不属于控件投影，排除
            if (entityManager.HasComponent<SplitterPanelComponent>(entity))
                continue;

            // scope 过滤（scope 非空时仅报告该作用域）
            if (!string.IsNullOrEmpty(scope)
                && (!entityManager.TryGetComponent<ScopeComponent>(entity, out var scopeComp)
                    || scopeComp.Scope != scope))
            {
                continue;
            }

            var name = entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info)
                ? info.Name
                : string.Empty;
            var type = entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp)
                ? typeComp.Type ?? string.Empty
                : string.Empty;
            var entityScope = entityManager.TryGetComponent<ScopeComponent>(entity, out var sc)
                ? sc.Scope
                : string.Empty;

            // root 面板（root:{scope}）现已参与布局计算（LayoutSystem 以自身 HostBasis/ContainerSize 基准结算，
            // 见 TryGetParentContainerSize root 分支）——不再豁免，缺失/0 尺寸将显式上报（防「root 停默认尺寸」再次静默）。

            total++;

            if (entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var computed))
            {
                if (computed.Width > 0 && computed.Height > 0)
                {
                    valid++;
                }
                else
                {
                    zeroSized++;
                    problems.Add($"0尺寸实体: name={name} type={type} scope={entityScope} size={computed.Width}x{computed.Height}");
                }
            }
            else
            {
                missing++;
                problems.Add($"缺失布局: name={name} type={type} scope={entityScope}");
            }
        }

        var result = new LayoutConvergenceResult(total, valid, zeroSized, missing, problems);

        if (result.IsConverged)
        {
            FrontEndDrivenLog.Log("LayoutConvergenceReport",
                new
                {
                    scope = scope ?? "(all)",
                    total = result.Total,
                    valid = result.Valid,
                    zeroSized = result.ZeroSized,
                    missing = result.Missing
                });
        }
        else
        {
            FrontEndDrivenLog.Log("LayoutConvergenceReport_violated",
                new
                {
                    scope = scope ?? "(all)",
                    total = result.Total,
                    valid = result.Valid,
                    zeroSized = result.ZeroSized,
                    missing = result.Missing,
                    problems = result.Problems
                });
        }

        return result;
    }
}
