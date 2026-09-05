namespace Modules.LayoutAnalysis.UIOverlapDetector.Models;

public class OverlapAnalysisResult
{
    public List<OverlapPair> AllPairs { get; set; } = new();

    public List<OverlapPair> ProblematicPairs { get; set; } = new();

    public int TotalControls { get; set; }

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    public bool HasProblematicOverlaps => ProblematicPairs.Count > 0;

    public int ProblematicOverlapCount => ProblematicPairs.Count;

    public int TotalOverlapCount => AllPairs.Count;

    public string FormatSummary()
    {
        if (!HasProblematicOverlaps)
            return $"[OK] 未检测到控件重叠（共 {TotalControls} 个控件）";

        var lines = new List<string>
        {
            $"[OVERLAP DETECTED] 发现 {ProblematicOverlapCount} 处控件重叠（共 {TotalControls} 个控件）:"
        };

        foreach (var pair in ProblematicPairs)
        {
            lines.Add($"  ⚠ {pair.GetDescription()}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
