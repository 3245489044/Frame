using System.Drawing;
using UniversalLayoutMath;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Models;

public class OverlapPair
{
    public string ControlAName { get; set; } = string.Empty;

    public string ControlBName { get; set; } = string.Empty;

    public string ControlAType { get; set; } = string.Empty;

    public string ControlBType { get; set; } = string.Empty;

    public Rectangle ControlABounds { get; set; }

    public Rectangle ControlBBounds { get; set; }

    public Rectangle Intersection { get; set; }

    public int OverlapArea { get; set; }

    public double OverlapPercentage { get; set; }

    public OverlapLevel Severity { get; set; } = OverlapLevel.None;

    public bool IsParentChild { get; set; }

    public string? ControlAParentName { get; set; }

    public string? ControlBParentName { get; set; }

    public string GetDescription()
    {
        var severityText = Severity switch
        {
            OverlapLevel.Complete => "完全覆盖",
            OverlapLevel.Severe => "大部分重叠",
            OverlapLevel.Moderate => "部分重叠",
            OverlapLevel.Slight => "轻微重叠",
            _ => "未知"
        };

        var parentChildTag = IsParentChild ? " [父子关系]" : "";

        return $"[{ControlAType}]「{ControlAName}」与 [{ControlBType}]「{ControlBName}」{severityText} ({OverlapPercentage:P1}){parentChildTag}";
    }
}
