#nullable enable

using System;
using System.Collections.Generic;
using Modules.Node.Gather;
using Share.Conversation.Rendering;

namespace Share.Conversation.Presentation;

/// <summary>
/// 聚拢声明 → 帧语义表（<see cref="FramePolicy"/>）的**唯一构造点**（框架级、零领域文案）。
/// <para>
/// 存在理由（SingleImplementation）：该装配此前在产品侧（<c>ObservabilityAttachClassification</c>）
/// 与分组/锚点规则混写，导致「声明投影」与「宿主策略」互相回流。现拆为：
/// 本工厂只做 spec → kind→角色/徽标 的机械投影；宿主只注入文案（<see cref="FrameGroupLabels"/>）与声明来源。
/// </para>
/// </summary>
public static class FramePolicyFactory
{
    /// <summary>由聚拢声明构造语义表（未声明角色的 kind ⇒ 普通帧；不丢弃、不猜）。</summary>
    /// <param name="spec">聚拢声明。</param>
    /// <param name="labels">分组/摘要文案（宿主注入；框架不持有领域词）。</param>
    /// <param name="presentation">呈现声明（宿主声明面解析所得）。</param>
    public static FramePolicy FromSpec(
        NodeGatherSpec spec, FrameGroupLabels labels, PresentationDeclaration presentation)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(presentation);

        var kinds = new Dictionary<string, FrameRoleSpec>(StringComparer.Ordinal);
        foreach (var pair in spec.Shapings)
        {
            if (pair.Key == NodeGatherSpec.DefaultShapingKey)
            {
                continue;
            }

            var role = string.IsNullOrWhiteSpace(pair.Value.Role) ? FrameRoleIds.Plain : pair.Value.Role!;
            kinds[pair.Key] = new FrameRoleSpec(pair.Key, role, pair.Value.Badge);
        }

        return new FramePolicy(kinds, labels, presentation);
    }

    /// <summary>声明是否存在可解析的呈现词（装配期可见化用；不参与身份判定）。</summary>
    /// <param name="spec">聚拢声明。</param>
    public static int CountDeclaredPresentation(NodeGatherSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var count = 0;
        foreach (var pair in spec.Shapings)
        {
            if (pair.Key == NodeGatherSpec.DefaultShapingKey) continue;
            if (!string.IsNullOrWhiteSpace(pair.Value.Presentation)) count++;
        }

        return count;
    }
}
