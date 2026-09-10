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

                // S2·坐标换算单一出口：统一经 LayoutCoordinateResolver.ToFormRelative（父链 .Location 累加，不含窗体）——
                // 与可见性检查/位置报告同基准（form 相对），消除 RectangleToScreen 的 handle 状态敏感
                // 与嵌套重复累加伪影（terminalPanel 屏幕坐标 1832→2469 的根因）。
                // 字段名 ScreenLeft/ScreenTop 保留（兼容既有消费方），语义 = form 相对统一基准（平移不变量，重叠判定等价）。
                var formRelative = LayoutCoordinateResolver.ToFormRelative(state.Control);
                metadata.ScreenLeft = formRelative.Left;
                metadata.ScreenTop = formRelative.Top;

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