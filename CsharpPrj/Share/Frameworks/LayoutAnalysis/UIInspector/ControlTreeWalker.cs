#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Framework.Tree;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class ControlTreeWalker
    {
        private readonly ReflectionExtractor _extractor;

        private readonly struct TraversalState
        {
            public readonly Control Control;
            public readonly string ParentName;

            public TraversalState(Control control, string parentName)
            {
                Control = control;
                ParentName = parentName;
            }

            public ControlMetadata Extract(ReflectionExtractor extractor)
                => extractor.ExtractControlMetadata(Control, ParentName);
        }

        public ControlTreeWalker()
        {
            _extractor = new ReflectionExtractor();
        }

        public List<ControlMetadata> Walk(Control root, string parentName = "")
        {
            ArgumentNullException.ThrowIfNull(root);

            return WalkCore(new TraversalState(root, parentName),
                TraversalStrategy.DepthFirst);
        }

        public List<ControlMetadata> WalkBreadthFirst(Control root)
            => Walk(root, string.Empty, TraversalStrategy.BreadthFirst);

        private List<ControlMetadata> Walk(Control root, string parentName, TraversalStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(root);

            return WalkCore(new TraversalState(root, parentName), strategy);
        }

        private List<ControlMetadata> WalkCore(TraversalState root, TraversalStrategy strategy)
        {
            var walker = new TreeWalker<TraversalState>(state =>
            {
                if (state.Control == null)
                    return Enumerable.Empty<TraversalState>();

                return state.Control.Controls
                    .Cast<Control>()
                    .Select(child => new TraversalState(child, ExtractNameFallback(state.Control)))
                    .ToList();
            });

            var result = new List<ControlMetadata>();
            walker.Walk(root, (state, _) =>
            {
                var metadata = state.Extract(_extractor);

                // S2·坐标换算单一出口：唯一口径 = form 相对（含 SplitterPanel 修正），见 LayoutCoordinateResolver。
                // 与可见性检查/位置报告同基准，消除 RectangleToScreen 的 handle 状态敏感与嵌套重复累加伪影。
                var formRelative = LayoutCoordinateResolver.ToFormRelative(state.Control);
                metadata.FormLeft = formRelative.Left;
                metadata.FormTop = formRelative.Top;

                result.Add(metadata);
            }, new TraversalOptions { Strategy = strategy });

            return result;
        }

        private static string ExtractNameFallback(Control control)
        {
            var name = control.Name;
            return string.IsNullOrEmpty(name) ? control.GetType().Name : name;
        }
    }
}