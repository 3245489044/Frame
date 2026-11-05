#nullable enable

using System.Collections.Generic;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class ControlMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;

        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// 唯一坐标口径：form 相对（父链累加，不含窗体自身；SplitterPanel 经 SplitterDistance 修正）。
        /// 由 <see cref="LayoutCoordinateResolver.ToFormRelative"/> 产出，全项目（含重叠判定/可见性/位置报告）只认此基准。
        /// </summary>
        public int FormLeft { get; set; }

        /// <summary>唯一坐标口径的纵轴，见 <see cref="FormLeft"/>。</summary>
        public int FormTop { get; set; }

        /// <summary>
        /// 历史兼容别名（LayoutAnalysis.md §4.9：名为 Screen 实为 form 相对）。
        /// 语义等同 <see cref="FormLeft"/>，新代码一律用 FormLeft；保留 setter 以兼容既有的对象初始化器。
        /// </summary>
        public int ScreenLeft
        {
            get => FormLeft;
            set => FormLeft = value;
        }

        /// <summary>历史兼容别名，见 <see cref="ScreenLeft"/>。</summary>
        public int ScreenTop
        {
            get => FormTop;
            set => FormTop = value;
        }

        public string Text { get; set; } = string.Empty;
        public string ParentName { get; set; } = string.Empty;
        public bool HasChildren { get; set; }
        public Dictionary<string, object?> AdditionalProperties { get; set; } = new();
    }
}