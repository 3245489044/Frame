#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Modules.CapabilityResolution;
using Modules.Gather;
using Share.Conversation.Rendering.Surface;
using Share.Conversation.Rendering.Surface.Selection;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 表面聚拢模块（产品无关）：提供六个中性域，producer 由装配期注入。
/// <para>域：surface-program / surface-selection / surface-map / surface-capabilities / surface-plan / surface-degradations。</para>
/// <para>本模块只产不用：计划不接渲染；未声明域（视图未要求）⇒ <see cref="GatherModuleBuilder.ProvideFor"/> 自动跳过。</para>
/// </summary>
public sealed class SurfaceGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly Func<SurfaceProgram?> _program;
    private readonly ISurfaceSelectionSource _selection;
    private readonly string _carrier;

    /// <summary>构造聚拢模块。</summary>
    /// <param name="view">聚拢视图声明。</param>
    /// <param name="program">表面程序取值（未装配 ⇒ null）。</param>
    /// <param name="selection">选择源（装配期注入）。</param>
    /// <param name="carrier">载体 id（由产品视图映射）。</param>
    public SurfaceGatherModule(
        GatherViewSpec view,
        Func<SurfaceProgram?> program,
        ISurfaceSelectionSource selection,
        string carrier)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _carrier = carrier ?? string.Empty;
    }

    /// <inheritdoc />
    public string Name => "surface-gather";

    /// <summary>本次装配的载体 id（诊断用）。</summary>
    public string Carrier => _carrier;

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.FromView(_view);

        builder.ProvideFor(_view, "surface-program", (_, _) => Task.FromResult<object?>(_program()));
        builder.ProvideFor(_view, "surface-selection", (_, _) => Task.FromResult<object?>(Selection(_program())));
        builder.ProvideFor(_view, "surface-map", (_, _) => Task.FromResult<object?>(MapOf(_program())));
        builder.ProvideFor(_view, "surface-capabilities", (_, _) => Task.FromResult<object?>(CapabilitiesOf(_program())));
        builder.ProvideFor(_view, "surface-plan", (_, _) => Task.FromResult<object?>(PlanOf(_program())));
        builder.ProvideFor(_view, "surface-degradations", (_, _) => Task.FromResult<object?>(DegradationsOf(_program())));
    }

    private SurfaceSelection Selection(SurfaceProgram? program)
        => _selection.Select(_view.View, _carrier);

    private static CapabilityMatrix CapabilitiesOf(SurfaceProgram? program)
        => program is null || !program.IsDeclared
            ? CapabilityMatrix.Empty
            : program.Capabilities;

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MapOf(SurfaceProgram? program)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (program is null || !program.IsDeclared) return map;

        foreach (var mapping in program.Mappings.Values)
        {
            var carrier = mapping.Carrier(_carrier);
            if (carrier is not null)
            {
                map[mapping.Id] = carrier;
            }
        }

        return map;
    }

    /// <summary>
    /// 解析计划：按**表面视图**（history/task/node…）逐一解析当前载体。
    /// <para>聚拢视图键（render-surface-html…）与表面视图键不是同一坐标；一个载体计划覆盖程序内全部表面视图。</para>
    /// </summary>
    private IReadOnlyDictionary<string, ResolvedSurface> PlanOf(SurfaceProgram? program)
    {
        var plans = new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);
        if (program is null || !program.IsDeclared) return plans;

        var selection = _selection.Select(_view.View, _carrier);
        var overrides = selection.Overrides;
        foreach (var view in program.Views.Keys)
        {
            plans[view] = SurfaceResolver.Resolve(
                program.Declaration,
                view,
                _carrier,
                selection.ProfileId,
                tokens: null,
                overrides);
        }

        return plans;
    }

    private IReadOnlyList<ResolutionIssue> DegradationsOf(SurfaceProgram? program)
    {
        var issues = new List<ResolutionIssue>();
        if (program is null || !program.IsDeclared)
        {
            issues.Add(new ResolutionIssue("program-unavailable", "surface.generated.json 未装配"));
            return issues;
        }

        foreach (var plan in PlanOf(program))
        {
            foreach (var issue in plan.Value.Issues)
            {
                issues.Add(new ResolutionIssue(issue.Code, plan.Key + ":" + issue.Detail));
            }

            foreach (var container in plan.Value.Containers)
            {
                foreach (var issue in container.Issues)
                {
                    issues.Add(new ResolutionIssue(issue.Code, plan.Key + ":" + container.Id + ":" + issue.Detail));
                }

                foreach (var material in container.Materials.Values)
                {
                    foreach (var issue in material.Issues)
                    {
                        issues.Add(new ResolutionIssue(
                            issue.Code,
                            plan.Key + ":" + container.Id + ":" + material.Slot + ":" + issue.Detail));
                    }
                }
            }
        }

        return issues
            .GroupBy(issue => issue.Code + "|" + issue.Detail, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }
}
