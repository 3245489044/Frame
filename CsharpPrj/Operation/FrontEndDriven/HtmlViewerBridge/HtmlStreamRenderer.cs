#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Frame;
using Share.Conversation;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;
using Share.Conversation.Rendering.Surface;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 通用会话消息流渲染器（**薄编排器**）：面序编排 + 能力判定；
/// 投影由声明驱动的表面管线（<see cref="SurfaceRenderPipeline"/> / <see cref="FrameComposer"/>）承担，
/// 附件行为无状态投影（<see cref="AttachProjector"/>）。
/// <para>纪律：产物 = 节点树（字符串出口唯一 = <see cref="UiFragmentWriter"/>）；
/// 能力经 <see cref="TemplateSurface"/> 一次解析（模板键来自声明）；折叠态由 <see cref="FoldPolicy"/> 裁决；
/// 缺声明/缺能力 = 可见降级（禁伪造等价结构）。</para>
/// </summary>
public sealed class HtmlStreamRenderer : IConversationFragmentRenderer
{
    private readonly HtmlStreamRendererOptions _options;
    private readonly Func<IReadOnlyDictionary<string, string>>? _templateProvider;
    private readonly FramePolicy? _policy;
    private readonly PresentationRegions _regions;

    public HtmlStreamRenderer(
        Func<IReadOnlyDictionary<string, string>>? templateProvider = null,
        HtmlStreamRendererOptions? options = null)
    {
        _options = options ?? new HtmlStreamRendererOptions();
        _templateProvider = templateProvider;
        // 单一语义表：宿主注入（声明装配点唯一 = FramePolicyFactory）；未注入 ⇒ 框架默认（零领域词）。
        _policy = _options.Policy;
        _regions = _options.Regions ?? PresentationRegions.Empty;
    }

    /// <summary>生效模板集：延迟解析（宿主注入）；null → 空集。</summary>
    private IReadOnlyDictionary<string, string> EffectiveTemplates
        => _templateProvider?.Invoke() ?? new Dictionary<string, string>();

    /// <summary>声明态降级原因（空 = 正常）：每项一个可见降级片段。</summary>
    private IReadOnlyList<string> Degradations(FoldPolicy fold)
    {
        var reasons = new List<string>(3);
        if (_options.RequireFramePolicy && _options.Policy is null)
            reasons.Add(HtmlStreamRendererOptions.FramePolicyUnavailableCode);
        if (!_regions.IsDeclared)
            reasons.Add(HtmlStreamRendererOptions.RegionsUndeclaredCode);
        if (fold.Degradation is { Length: > 0 } degradation)
            reasons.Add(degradation!);
        return reasons;
    }

    /// <summary>整段渲染（旧 string 端口）：段序列化拼接。</summary>
    public string Render(IReadOnlyList<ConversationRenderItem> items)
        => UiFragmentWriter.SerializeAll(
            RenderFragments(items).Select(fragment => fragment.Root).ToList());

    /// <summary>分段渲染（旧 string 端口）：节点片段逐段序列化（唯一字符串出口）。</summary>
    public IReadOnlyList<ConversationRenderSection> RenderSections(IReadOnlyList<ConversationRenderItem> items)
        => RenderFragments(items)
            .Select(fragment => new ConversationRenderSection(fragment.Key, UiFragmentWriter.Serialize(fragment.Root)))
            .ToList();

    /// <summary>节点渲染（**唯一实现**）：按声明面序产出各面片段；段键 = 流差分单位。</summary>
    public IReadOnlyList<ConversationRenderFragment> RenderFragments(
        IReadOnlyList<ConversationRenderItem> items, ConversationRenderContext? context = null)
    {
        var surface = TemplateSurface.From(EffectiveTemplates, _regions);
        var labels = _policy?.Labels ?? FrameGroupLabels.Default;
        var fold = context?.Fold ?? _options.Fold ?? FoldPolicy.Default;
        var projection = new ConversationProjectionContext(
            surface,
            _policy,
            labels,
            fold,
            context?.RoundLabels,
            _options.EmptyText);

        // 段键与投影分离：划分唯一实现 = ConversationSectionPartitioner；投影只有一条路径。
        var sections = FrontEndDriven.HtmlViewerBridge.Surface.ConversationSectionPartitioner.Partition(
            items, projection, Degradations(fold), _options, _regions);
        return FrontEndDriven.HtmlViewerBridge.Surface.SurfaceFragmentPlanner.Plan(
            sections, projection, _options.Surface, context?.ViewKey);
    }
}

/// <summary>通用消息流渲染器的可配置项（调用方注入）；缺省保持默认气泡输出。</summary>
public sealed record HtmlStreamRendererOptions
{
    /// <summary>语义表声明不可用诊断码（声明面 <c>ui/diagnostics.json</c>）。</summary>
    public const string FramePolicyUnavailableCode = "frame-policy-unavailable";

    /// <summary>渲染区域声明不可用诊断码（模板键无法解析 ⇒ 可见降级）。</summary>
    public const string RegionsUndeclaredCode = "regions-undeclared";

    /// <summary>空历史文案（空态；宿主注入本地化值，桥内不含领域文案）。</summary>
    public string EmptyText { get; init; } = "empty";

    /// <summary>渲染面编排序（结构覆盖；null ⇒ 声明序 <see cref="RenderSurfaceCatalog.OrderOf"/>）。</summary>
    public IReadOnlyList<RenderSurface>? SurfaceOrder { get; init; }

    /// <summary>头提示层按缓存带顺序渲染开关（true ⇒ 声明段整体前置为会话级块）。</summary>
    public bool HeadBandOrdering { get; init; }

    /// <summary>操作流 / 气泡流分离开关：true（默认）⇒ 操作面渲染为顶部操作面板（非气泡）。</summary>
    public bool SplitOperationChannel { get; init; } = true;

    /// <summary>过程帧**语义表**（宿主注入，唯一注入面，装配点 = <c>FramePolicyFactory.FromSpec</c>）：
    /// kind → 角色/徽标 + 组文案。null ⇒ 扁平回退（不猜分组）。</summary>
    public FramePolicy? Policy { get; init; }

    /// <summary>是否要求语义表：true 且 <see cref="Policy"/> 为空 ⇒ 可见降级（禁静默换语义）。</summary>
    public bool RequireFramePolicy { get; init; }

    /// <summary>渲染区域/模板键声明（模板键唯一来源；null ⇒ 空声明，可见降级）。</summary>
    public PresentationRegions? Regions { get; init; }

    /// <summary>折叠策略（宿主按视图声明注入；null ⇒ <see cref="FoldPolicy.Default"/>）。</summary>
    public FoldPolicy? Fold { get; init; }

    /// <summary>渲染声明闭包（宿主注入；null ⇒ 可见降级，禁回退旧投影器）。</summary>
    public SurfaceDeclarations? Surface { get; init; }
}


