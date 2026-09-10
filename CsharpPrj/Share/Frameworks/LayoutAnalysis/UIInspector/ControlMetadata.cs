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
        /// 统一基准坐标（S2·LayoutCoordinateResolver.ToFormRelative 产出）：form 相对（父链 .Location 累加，不含窗体）。
        /// 字段名保留历史名 ScreenLeft/ScreenTop，语义已由「屏幕绝对」收敛为「form 相对统一基准」——
        /// 与可见性检查/位置报告同基准（平移不变量，OverlapCalculator 重叠判定等价）。
        /// </summary>
        public int ScreenLeft { get; set; }

        public int ScreenTop { get; set; }

        public string Text { get; set; } = string.Empty;
        public string ParentName { get; set; } = string.Empty;
        public bool HasChildren { get; set; }
        public Dictionary<string, object?> AdditionalProperties { get; set; } = new();
    }
}