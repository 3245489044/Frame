#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Modules.Gather;
using Share.Conversation.Rendering.Surface.Selection;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 声明域聚拢模块（**材质/结构唯一来源**）：surface-program / surface-selection / surface-plan / surface-degradations。
/// <para>产物 = <see cref="SurfacePlanBundle"/>（可被 Composer 直接消费）；解析只在这里发生一次。</para>
/// <para>纪律：本模块只产不用；未声明域由 <c>ProvideFor</c> 自动跳过。</para>
/// </summary>
public sealed class SurfacePlanGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly Func<SurfaceProgram?> _program;
    private readonly ISurfaceSelectionSource _selection;
    private readonly string _carrier;
    private readonly SurfaceFrameContext _context;

    /// <summary>构造模块。</summary>
    /// <param name="view">聚拢视图声明。</param>
    /// <param name="program">表面程序取值（未装配 ⇒ null）。</param>
    /// <param name="selection">选择源。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="context">帧作用域共享槽（写 Plan）。</param>
    public SurfacePlanGatherModule(
        GatherViewSpec view,
        Func<SurfaceProgram?> program,
        ISurfaceSelectionSource selection,
        string carrier,
        SurfaceFrameContext context)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _carrier = carrier ?? string.Empty;
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public string Name => "surface-plan-gather";

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.FromView(_view);

        builder.ProvideFor(_view, "surface-program", (_, _) => Task.FromResult<object?>(_program()));
        builder.ProvideFor(_view, "surface-selection",
            (_, _) => Task.FromResult<object?>(_selection.Select(_view.View, _carrier)));
        builder.ProvideFor(_view, "surface-plan", (_, _) => Task.FromResult<object?>(Bundle()));
        builder.ProvideFor(_view, "surface-degradations", (_, _) => Task.FromResult<object?>(Degradations()));
    }

    /// <summary>组装本载体在全部表面视图上的解析结果（唯一来源）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="view">表面视图键（仅用于诊断；产物覆盖程序内全部视图）。</param>
    public SurfacePlanBundle Bundle(string carrier, string view)
        => Build(carrier);

    private SurfacePlanBundle Bundle()
    {
        var bundle = Build(_carrier);
        _context.SetPlan(bundle);
        return bundle;
    }

    private SurfacePlanBundle Build(string carrier)
    {
        var program = _program();
        if (program is null || !program.IsDeclared)
        {
            return new SurfacePlanBundle(carrier, string.Empty, EmptyViews(), Array.Empty<SurfaceIssue>());
        }

        var selection = _selection.Select(_view.View, carrier);
        var views = new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);
        var issues = new List<SurfaceIssue>();
        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(
                program.Declaration,
                view,
                carrier,
                selection.ProfileId,
                tokens: null,
                selection.Overrides);
            views[view] = resolved;
            foreach (var issue in resolved.Issues)
            {
                issues.Add(new SurfaceIssue(issue.Code, view + ":" + issue.Detail));
            }

            foreach (var container in resolved.Containers)
            {
                foreach (var issue in container.Issues)
                {
                    issues.Add(new SurfaceIssue(issue.Code, view + ":" + container.Id + ":" + issue.Detail));
                }

                foreach (var material in container.Materials.Values)
                {
                    foreach (var issue in material.Issues)
                    {
                        issues.Add(new SurfaceIssue(
                            issue.Code, view + ":" + container.Id + ":" + material.Slot + ":" + issue.Detail));
                    }
                }
            }
        }

        return new SurfacePlanBundle(carrier, selection.ProfileId, views, Distinguish(issues))
        {
            Tokens = SurfaceResolver.TokensForProfile(program.Declaration, selection.ProfileId),
            RoleSlots = RoleSlotsOf(program.Declaration),
        };
    }

    private static IReadOnlyDictionary<string, string> RoleSlotsOf(SurfaceProgramDeclaration declaration)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in new[] { "host", "card", "nested", "leaf" })
        {
            if (declaration.Settings.TryGetValue("role_" + role + "_slot", out var slot) && slot.Length > 0)
            {
                slots[role] = slot;
            }
        }

        return slots;
    }

    private IReadOnlyList<SurfaceIssue> Degradations()
    {
        var bundle = Build(_carrier);
        return bundle.Degradations;
    }

    private static IReadOnlyDictionary<string, ResolvedSurface> EmptyViews()
        => new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);

    private static IReadOnlyList<SurfaceIssue> Distinguish(List<SurfaceIssue> issues)
    {
        if (issues.Count == 0) return Array.Empty<SurfaceIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SurfaceIssue>(issues.Count);
        foreach (var issue in issues)
        {
            if (seen.Add(issue.Code + "|" + issue.Detail)) result.Add(issue);
        }

        return result;
    }
}
