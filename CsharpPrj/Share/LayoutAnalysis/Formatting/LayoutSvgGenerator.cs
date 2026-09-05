#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using Modules.LayoutAnalysis.Models;
using Plate.XmlMethod.Svg;

namespace Modules.LayoutAnalysis.Formatting
{
    /// <summary>SVG 渲染的尺寸/样式选项。缺省即用内置默认值。</summary>
    public sealed class LayoutSvgOptions
    {
        public double NodeWidth { get; set; } = 180;
        public double NodeHeight { get; set; } = 44;
        public double GapX { get; set; } = 40;
        public double GapY { get; set; } = 30;
        public double PaddingX { get; set; } = 20;
        public double PaddingY { get; set; } = 20;
        public double FontSize { get; set; } = 13;
        public string Background { get; set; } = "#ffffff";
        /// <summary>是否在边上标注 RelationType（onSuccess/onFail/...）。</summary>
        public bool ShowEdgeLabels { get; set; } = true;
    }

    /// <summary>
    /// 把 <see cref="LayoutGraphContext"/> 渲染成自包含 SVG / HTML。
    /// 算法：edge（source→target）最长路径分层布局（Sugiyama 简化版，先入度为 0 处定层，迭代松弛累加），
    /// 同层排 x、跨层排 y，最终产出 SVG 字符串。产物交给 HtmlViewer(NavigateToString) 整页注入即可显示。
    /// 边样式按 <see cref="LayoutEdgeData.RelationType"/> 语义派生：fail 红、degrade/unavailable 橙虚线、max/retry 蓝、其余为主链默认。
    /// </summary>
    public static class LayoutSvgGenerator
    {
        // ---- 入口 ----

        public static string GenerateSvg(LayoutGraphContext context, LayoutSvgOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            options ??= new LayoutSvgOptions();

            var layout = ComputeLayout(context, options);
            return RenderSvg(layout, context, options);
        }

        /// <summary>生成整页 HTML（自包含 SVG 内联），可直接交给 htmlviewer。</summary>
        public static string GenerateHtml(LayoutGraphContext context, LayoutSvgOptions? options = null)
        {
            options ??= new LayoutSvgOptions();
            return Svg.WrapHtml(GenerateSvg(context, options), title: "workflow-graph", background: options.Background);
        }

        // ---- 布局 ----

        /// <summary>布局后的节点（含像素坐标）。</summary>
        private sealed class PlacedNode
        {
            public string Id;
            public double X;
            public double Y;
            public int Layer;
        }

        private sealed class PlacedEdge
        {
            public string From;
            public string To;
            public string Label = string.Empty;
            public (string Color, bool Dashed) Style;
        }

        private sealed class Layout
        {
            public List<PlacedNode> Nodes = new();
            public List<PlacedEdge> Edges = new();
            public double CanvasWidth;
            public double CanvasHeight;
            public HashSet<string> EntryNodeIds = new();
        }

        private static Layout ComputeLayout(LayoutGraphContext context, LayoutSvgOptions o)
        {
            var graph = context.Graph;
            var nodeKeys = graph.GetAllNodeKeys().ToList();
            var keySet = new HashSet<string>(nodeKeys);

            // 收集边；跳过指向不存在节点的悬空边
            var edges = new List<(string From, string To, LayoutEdgeData Data)>();
            foreach (var edgeId in graph.GetAllEdgeIds())
            {
                var src = graph.GetEdgeSource(edgeId);
                var dst = graph.GetEdgeTarget(edgeId);
                if (!keySet.Contains(src) || !keySet.Contains(dst)) continue;
                edges.Add((src, dst, graph.GetEdgeData(edgeId) ?? new LayoutEdgeData()));
            }

            // 入边表（按 to 归纳 from 列表）
            var fromMap = new Dictionary<string, List<string>>();
            foreach (var e in edges)
            {
                if (!fromMap.TryGetValue(e.To, out var list))
                    fromMap[e.To] = list = new List<string>();
                list.Add(e.From);
            }

            // 最长路径分层：layer[n]=0，若无入边；否则 max(layer[p]+1)。DAG 下迭代松弛收敛。
            var layer = new Dictionary<string, int>();
            foreach (var n in nodeKeys) layer[n] = 0;
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var n in nodeKeys)
                    {
                        int best = -1;
                        if (fromMap.TryGetValue(n, out var parents))
                        {
                            foreach (var p in parents)
                                if (layer[p] > best) best = layer[p];
                        }
                        int target = best + 1;
                        if (target > layer[n])
                        {
                            layer[n] = target;
                            changed = true;
                        }
                    }
                }
            }

            // 按层分组
            var byLayer = nodeKeys
                .GroupBy(n => layer[n])
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            var placed = new List<PlacedNode>();
            double canvasWidth = o.PaddingX * 2;
            for (int li = 0; li < byLayer.Count; li++)
            {
                var group = byLayer[li];
                double y = o.PaddingY + li * (o.NodeHeight + o.GapY);
                for (int i = 0; i < group.Count; i++)
                {
                    double x = o.PaddingX + i * (o.NodeWidth + o.GapX);
                    placed.Add(new PlacedNode { Id = group[i], X = x, Y = y, Layer = li });
                }
                double layerWidth = o.PaddingX + group.Count * (o.NodeWidth + o.GapX);
                if (layerWidth > canvasWidth) canvasWidth = layerWidth;
            }
            double canvasHeight = o.PaddingY + byLayer.Count * (o.NodeHeight + o.GapY);

            // 边样式派生
            var placedEdges = edges
                .Select(e => new PlacedEdge
                {
                    From = e.From,
                    To = e.To,
                    Label = e.Data.RelationType ?? e.Data.Kind ?? string.Empty,
                    Style = StyleFor(e.Data),
                })
                .ToList();

            var entry = new HashSet<string>(context.EntryNodeIds);
            // 若未探测出入口，补一个入度为 0 的首个节点作入口高亮
            if (entry.Count == 0)
            {
                var indegree = new HashSet<string>(edges.Select(e => e.To));
                var first = nodeKeys.FirstOrDefault(n => !indegree.Contains(n));
                if (first != null) entry.Add(first);
            }

            return new Layout
            {
                Nodes = placed,
                Edges = placedEdges,
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                EntryNodeIds = entry,
            };
        }

        private static (string Color, bool Dashed) StyleFor(LayoutEdgeData d)
        {
            var rt = d.RelationType ?? string.Empty;
            var kind = d.Kind ?? string.Empty;
            string token;
            if (rt.Length > 0) token = rt; else token = kind;
            token = token.ToLowerInvariant();

            if (token.Contains("fail"))
                return ("#e5484d", false);          // 失败：红
            if (token.Contains("navailable") || token.Contains("degrade") || token.Contains("fallback"))
                return ("#f5a524", true);            // 降级：橙虚线
            if (token.Contains("max") || token.Contains("retry") || token.Contains("backoff"))
                return ("#3b82f6", false);           // 重试：蓝
            return ("#2563eb", false);               // 成功主链：高亮蓝
        }

        // ---- SVG 渲染 ----

        private static string RenderSvg(Layout layout, LayoutGraphContext context, LayoutSvgOptions o)
        {
            var pos = layout.Nodes.ToDictionary(n => n.Id, n => n);
            var svg = Svg.Root(layout.CanvasWidth, layout.CanvasHeight);

            // 箭头 marker
            var defs = Svg.Defs()
                .Child(Svg.ArrowMarker("arrow"));
            svg.Child(defs);

            // 边（先画，位于节点之下）
            foreach (var e in layout.Edges)
            {
                if (!pos.TryGetValue(e.From, out var s) || !pos.TryGetValue(e.To, out var t)) continue;
                var (x1, y1) = RightMid(s, o);
                var (x2, y2) = LeftMid(t, o);
                var line = Svg.Line(x1, y1, x2, y2)
                    .Stroke(e.Style.Color, 1.6)
                    .Attr("marker-end", "url(#arrow)");
                if (e.Style.Dashed) line.Dash();
                svg.Child(line);

                if (o.ShowEdgeLabels && e.Label.Length > 0)
                {
                    svg.Child(Svg.Text((x1 + x2) / 2, (y1 + y2) / 2 - 4, e.Label, "middle")
                        .Font(10).Fill(e.Style.Color));
                }
            }

            // 节点
            foreach (var n in layout.Nodes)
            {
                bool isEntry = layout.EntryNodeIds.Contains(n.Id);
                var rect = Svg.Rect(n.X, n.Y, o.NodeWidth, o.NodeHeight, 6)
                    .Fill("#f8fafc")
                    .Stroke(isEntry ? "#1d4ed8" : "#94a3b8", isEntry ? 2.2 : 1.2);
                svg.Child(rect);

                var label = LayoutLabel(context, n.Id, o);
                svg.Child(Svg.Text(n.X + o.NodeWidth / 2, n.Y + o.NodeHeight / 2 + 5, label, "middle")
                    .Fill("#1e293b").Font(o.FontSize));
            }

            return svg.Render();
        }

        private static string LayoutLabel(LayoutGraphContext context, string nodeId, LayoutSvgOptions o)
        {
            var data = context.Graph.GetNodeData(nodeId);
            if (data is null) return nodeId;

            // 优先取 Entity.Id（NormalizedEntity 以 Id 为名）；否则返回节点键
            var entityId = data.Entity?.Id;
            return string.IsNullOrEmpty(entityId) ? nodeId : entityId;
        }

        private static (double X, double Y) RightMid(PlacedNode n, LayoutSvgOptions o)
            => (n.X + o.NodeWidth, n.Y + o.NodeHeight / 2);

        private static (double X, double Y) LeftMid(PlacedNode n, LayoutSvgOptions o)
            => (n.X, n.Y + o.NodeHeight / 2);
    }
}