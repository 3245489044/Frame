#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 计划 → 节计划适配器（单向）：<see cref="UiWritePlan"/> 是既有对外契约，<see cref="SectionPlan"/> 是落地单位。
/// <para>本适配器同时承担结构估算（nodes/bytes），使预算与遥测共用同一口径（禁第二套记分牌）。</para>
/// </summary>
public static class SectionPlanAdapter
{
    /// <summary>把写计划展开为节计划（保持目标顺序；载荷形态决定节类型）。</summary>
    public static IReadOnlyList<SectionPlan> From(UiWritePlan? plan)
    {
        if (plan is null || plan.Kind == UiWriteKind.Skip) return Array.Empty<SectionPlan>();

        var list = new List<SectionPlan>(plan.Targets.Count);
        foreach (var target in plan.Targets)
        {
            var name = target.Name ?? string.Empty;
            switch (plan.Kind)
            {
                case UiWriteKind.Region when target.Payload is string html:
                    list.Add(HtmlSection(name, html, plan.Interaction));
                    break;
                case UiWriteKind.Tree when target.Payload is UiNode tree:
                    list.Add(TreeSection(name, tree, plan.Interaction));
                    break;
                case UiWriteKind.TreeAndLeaf when target.Payload is UiNode treeInMix:
                    list.Add(TreeSection(name, treeInMix, plan.Interaction));
                    break;
                case UiWriteKind.TreeAndLeaf when target.Payload is string valueInMix:
                    list.Add(LeafSection(name, valueInMix));
                    break;
                case UiWriteKind.Leaf when target.Payload is string leafValue:
                    list.Add(LeafSection(name, leafValue));
                    break;
                case UiWriteKind.Append when target.Payload is UiStreamAppend append:
                    list.Add(StreamSection(name, append));
                    break;
                case UiWriteKind.Trim when target.Payload is UiStreamTrim trim:
                    list.Add(TrimSection(name, trim));
                    break;
                case UiWriteKind.Composite:
                    list.Add(target.Payload switch
                    {
                        UiNode tree => TreeSection(name, tree, plan.Interaction),
                        UiStreamAppend append => StreamSection(name, append),
                        UiStreamTrim trim => TrimSection(name, trim),
                        string value => LeafSection(name, value),
                        _ => UnknownSection(name),
                    });
                    break;
                case UiWriteKind.FullDocument when target.Payload is string document:
                    list.Add(new SectionPlan(
                        new SectionId("document", name),
                        SectionWriteMode.Patch,
                        Html: document,
                        Nodes: CountHtmlNodes(document),
                        Bytes: document.Length,
                        FullDocument: true));
                    break;
                default:
                    list.Add(UnknownSection(name));
                    break;
            }
        }

        return list;
    }

    /// <summary>计划级节点数估算（遥测/预算预检；与节口径一致）。</summary>
    public static int EstimateNodes(UiWritePlan? plan)
    {
        var nodes = 0;
        foreach (var s in From(plan)) nodes += Math.Max(0, s.Nodes);
        return nodes;
    }

    /// <summary>计划级字节估算（与节口径一致）。</summary>
    public static int EstimateBytes(UiWritePlan? plan)
    {
        var bytes = 0;
        foreach (var s in From(plan)) bytes += Math.Max(0, s.Bytes);
        return bytes;
    }

    private static SectionPlan HtmlSection(string name, string html, UiInteractionSnapshot? interaction)
        => new(
            new SectionId("region", name),
            SectionWriteMode.Patch,
            Html: html,
            Nodes: CountHtmlNodes(html),
            Bytes: html.Length,
            Variance: SectionVariance.Stable,
            Interaction: interaction);

    private static SectionPlan TreeSection(string name, UiNode tree, UiInteractionSnapshot? interaction)
        => new(
            new SectionId("tree", name),
            SectionWriteMode.Patch,
            Tree: tree,
            Nodes: CountNodes(tree),
            Bytes: EstimateTreeBytes(tree),
            Variance: SectionVariance.Stable,
            Interaction: interaction);

    private static SectionPlan LeafSection(string name, string value)
        => new(new SectionId("leaf", name), SectionWriteMode.Patch, LeafValue: value, Nodes: 1, Bytes: value.Length);

    private static SectionPlan StreamSection(string name, UiStreamAppend append)
    {
        var bytes = 0;
        foreach (var item in append.Items) bytes += item.Key.Length + item.Html.Length;
        return new SectionPlan(
            new SectionId("stream", name),
            SectionWriteMode.Patch,
            Stream: append,
            Nodes: append.Items.Count,
            Bytes: bytes,
            Variance: SectionVariance.Volatile);
    }

    private static SectionPlan TrimSection(string name, UiStreamTrim trim)
        => new(new SectionId("stream", name), SectionWriteMode.Patch, Trim: trim, Bytes: 16, Variance: SectionVariance.Volatile);

    private static SectionPlan UnknownSection(string name)
        => new(new SectionId("unknown", name), SectionWriteMode.Patch, Variance: SectionVariance.Volatile);

    /// <summary>节点数（含文本/片段节点；O(n) 一次遍历）。</summary>
    public static int CountNodes(UiNode? node)
    {
        if (node is null) return 0;
        var count = 1;
        if (node.Children is null) return count;
        foreach (var child in node.Children) count += CountNodes(child);
        return count;
    }

    /// <summary>树字节估算（tag + 键 + 属性 + 文本；O(n)）。</summary>
    public static int EstimateTreeBytes(UiNode? node)
    {
        if (node is null) return 0;
        var bytes = node.Tag.Length + (node.Key?.Length ?? 0) + (node.Text?.Length ?? 0);
        if (node.Attributes is not null)
        {
            foreach (var kv in node.Attributes) bytes += kv.Key.Length + (kv.Value?.Length ?? 0);
        }

        if (node.Children is null) return bytes;
        foreach (var child in node.Children) bytes += EstimateTreeBytes(child);
        return bytes;
    }

    /// <summary>HTML 节点数估算（开标签计数，闭合/注释不计；O(n) 且零解析成本）。</summary>
    public static int CountHtmlNodes(string? html)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        var count = 0;
        for (var i = 0; i < html.Length - 1; i++)
        {
            if (html[i] != '<') continue;
            var next = html[i + 1];
            if (next == '/' || next == '!') continue;
            if (char.IsLetter(next)) count++;
        }

        return count;
    }
}
