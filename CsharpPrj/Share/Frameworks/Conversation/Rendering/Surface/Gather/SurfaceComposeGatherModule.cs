#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Modules.Gather;
using Share.SurfacePaint.Hosting;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 组合域聚拢模块：<c>surface-decor</c>（装饰计划唯一出口）+ <c>surface-viewport</c>（转发）。
/// <para>装饰输入 = <c>surface-plan</c> 产物（<see cref="SurfaceFrameContext.Plan"/>）+ 卡片几何（<see cref="SurfaceFrameContext.Cards"/>）。</para>
/// <para>阶段顺序由声明 <c>deps</c> 保证（input → decor）；本模块不自行排序。</para>
/// </summary>
public sealed class SurfaceComposeGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly SurfaceFrameContext _context;
    private readonly Func<SurfaceViewport> _viewport;
    private readonly Func<bool> _pageFieldAvailable;

    /// <summary>构造模块。</summary>
    /// <param name="view">聚拢视图声明。</param>
    /// <param name="context">帧作用域共享槽（读 Plan/Cards）。</param>
    /// <param name="viewport">窗口视口取值。</param>
    /// <param name="pageFieldAvailable">页场是否可用（由宿主注入；本层不引 Ambient）。</param>
    public SurfaceComposeGatherModule(
        GatherViewSpec view,
        SurfaceFrameContext context,
        Func<SurfaceViewport> viewport,
        Func<bool> pageFieldAvailable)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _pageFieldAvailable = pageFieldAvailable ?? throw new ArgumentNullException(nameof(pageFieldAvailable));
    }

    /// <inheritdoc />
    public string Name => "surface-compose-gather";

    /// <inheritdoc />
    public void Declare(GatherModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.FromView(_view);

        builder.ProvideFor(_view, "surface-decor", (_, _) => Task.FromResult<object?>(Decor()));
        builder.ProvideFor(_view, "surface-viewport", (_, _) => Task.FromResult<object?>(_viewport()));
    }

    /// <summary>按当前帧上下文组装装饰产物（唯一实现 = <see cref="SurfaceDecorPlan.Resolve"/>）。</summary>
    private SurfaceDecorBundle Decor()
    {
        var planBundle = _context.Plan;
        var cards = _context.Cards;
        if (cards.Count == 0) return SurfaceDecorBundle.Empty;

        var entries = new Dictionary<string, SurfaceDecorEntry>(StringComparer.Ordinal);
        var pageField = _pageFieldAvailable();
        foreach (var card in cards)
        {
            var material = planBundle.FindMaterial(card.MaterialSlot);
            var role = planBundle.DescribeCard(card.ContainerKey, card.MaterialSlot);
            if (material is null)
            {
                entries[card.Key] = SurfaceDecorEntry.Missing(card.Key, card.MaterialSlot);
                continue;
            }

            var roleName = role?.Role ?? "face";
            var tier = role?.Tier ?? "root";
            var depth = role?.Depth ?? card.DepthHint;
            var scope = ScopeOf(roleName, material);
            var decorPlan = SurfaceDecorPlan.Resolve(material, scope, pageField);
            entries[card.Key] = new SurfaceDecorEntry(
                card.Key, roleName, tier, depth, decorPlan, card.MaterialSlot,
                MissingDeclaration: decorPlan.Missing.Length > 0);
        }

        return new SurfaceDecorBundle(entries);
    }

    private static SurfaceDecorScope ScopeOf(string role, ResolvedMaterial material)
    {
        if (string.Equals(role, "host", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "region", StringComparison.OrdinalIgnoreCase))
        {
            return SurfaceDecorScope.PageHost;
        }

        return (material.Decoration & SurfaceDecoration.PageField) != 0
            ? SurfaceDecorScope.Material
            : SurfaceDecorScope.Face;
    }
}
