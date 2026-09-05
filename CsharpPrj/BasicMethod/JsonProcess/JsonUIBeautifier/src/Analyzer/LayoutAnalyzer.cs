using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Analyzer;

public class LayoutAnalyzer
{
    public double AnalyzeLayoutQuality(JObject root)
    {
        var layouts = LayoutHelper.GetLayoutValues(root);
        return LayoutMath.CalculateLayoutQuality(layouts);
    }

    public double AnalyzeSpacingConsistency(JObject root)
    {
        var layouts = LayoutHelper.GetLayoutValues(root);
        return LayoutMath.CalculateSpacingConsistency(layouts);
    }

    public List<(string Path1, string Path2, double OverlapArea)> DetectOverlaps(JObject root)
    {
        var controls = LayoutHelper.GetAllLayouts(root);
        var overlaps = new List<(string, string, double)>();

        for (int i = 0; i < controls.Count; i++)
        {
            for (int j = i + 1; j < controls.Count; j++)
            {
                var overlap = LayoutMath.CalculateOverlap(
                    (controls[i].L, controls[i].T, controls[i].W, controls[i].H),
                    (controls[j].L, controls[j].T, controls[j].W, controls[j].H));

                if (overlap > 0)
                {
                    overlaps.Add((
                        controls[i].Path,
                        controls[j].Path,
                        overlap
                    ));
                }
            }
        }

        return overlaps;
    }


    public List<LayoutPattern> RecognizePatterns(JObject root)
    {
        var patterns = new List<LayoutPattern>();

        foreach (var (path, container) in JsonNavigator.GetAllContainers(root))
        {
            var controls = container["controls"] as JArray;
            if (controls == null || controls.Count < 2) continue;

            var pattern = AnalyzeContainerPattern(controls, path);
            if (pattern != null)
                patterns.Add(pattern);
        }

        return patterns;
    }

    private LayoutPattern? AnalyzeContainerPattern(JArray controls, string containerPath)
    {
        var layouts = new List<(double L, double T, double W, double H)>();
        var types = new List<string>();

        foreach (var control in controls)
        {
            var (l, t, w, h) = JsonNavigator.GetLayout(control);
            if (l.HasValue && t.HasValue && w.HasValue && h.HasValue)
                layouts.Add((l.Value, t.Value, w.Value, h.Value));
            types.Add(JsonNavigator.GetControlType(control));
        }

        if (layouts.Count < 2) return null;

        if (IsFormRowPattern(types, layouts, out var confidence))
        {
            return new LayoutPattern
            {
                Type = LayoutPatternType.FormRow,
                ContainerPath = containerPath,
                Confidence = confidence,
                MemberControls = controls.Select(c => JsonNavigator.GetControlName(c)).ToList()
            };
        }

        if (IsToolbarPattern(types, layouts, out confidence))
        {
            return new LayoutPattern
            {
                Type = LayoutPatternType.Toolbar,
                ContainerPath = containerPath,
                Confidence = confidence,
                MemberControls = controls.Select(c => JsonNavigator.GetControlName(c)).ToList()
            };
        }

        if (IsVerticalStackPattern(layouts, out confidence))
        {
            return new LayoutPattern
            {
                Type = LayoutPatternType.VerticalStack,
                ContainerPath = containerPath,
                Confidence = confidence,
                MemberControls = controls.Select(c => JsonNavigator.GetControlName(c)).ToList()
            };
        }

        if (IsHorizontalRowPattern(layouts, out confidence))
        {
            return new LayoutPattern
            {
                Type = LayoutPatternType.HorizontalRow,
                ContainerPath = containerPath,
                Confidence = confidence,
                MemberControls = controls.Select(c => JsonNavigator.GetControlName(c)).ToList()
            };
        }

        return null;
    }

    private bool IsFormRowPattern(List<string> types, List<(double L, double T, double W, double H)> layouts, out double confidence)
    {
        confidence = 0;
        if (types.Count < 2) return false;

        int labelCount = types.Count(t => t == "label");
        int inputCount = types.Count(t => t is "textBox" or "comboBox" or "button");

        if (labelCount > 0 && inputCount > 0 && Math.Abs(labelCount - inputCount) <= 1)
        {
            confidence = 0.7 + (labelCount + inputCount) / (double)types.Count * 0.3;
            return true;
        }

        return false;
    }

    private bool IsToolbarPattern(List<string> types, List<(double L, double T, double W, double H)> layouts, out double confidence)
    {
        confidence = 0;
        if (types.Count < 2) return false;

        int buttonCount = types.Count(t => t == "button");
        if (buttonCount >= types.Count * 0.6)
        {
            var tops = layouts.Select(l => l.T).Distinct().ToList();
            if (tops.Count <= 2)
            {
                confidence = 0.8;
                return true;
            }
        }

        return false;
    }

    private bool IsVerticalStackPattern(List<(double L, double T, double W, double H)> layouts, out double confidence)
    {
        confidence = 0;
        if (layouts.Count < 2) return false;

        var lefts = layouts.Select(l => l.L).ToList();
        var widths = layouts.Select(l => l.W).ToList();
        var tops = layouts.Select(l => l.T).ToList();

        var leftVariance = lefts.Max() - lefts.Min();
        var widthVariance = widths.Max() - widths.Min();

        if (leftVariance < 0.1 && widthVariance < 0.1)
        {
            var isIncreasing = true;
            for (int i = 1; i < tops.Count; i++)
            {
                if (tops[i] <= tops[i - 1])
                {
                    isIncreasing = false;
                    break;
                }
            }

            if (isIncreasing)
            {
                confidence = 0.9;
                return true;
            }
        }

        return false;
    }

    private bool IsHorizontalRowPattern(List<(double L, double T, double W, double H)> layouts, out double confidence)
    {
        confidence = 0;
        if (layouts.Count < 2) return false;

        var tops = layouts.Select(l => l.T).ToList();
        var heights = layouts.Select(l => l.H).ToList();
        var lefts = layouts.Select(l => l.L).ToList();

        var topVariance = tops.Max() - tops.Min();
        var heightVariance = heights.Max() - heights.Min();

        if (topVariance < 0.05 && heightVariance < 0.05)
        {
            var isIncreasing = true;
            for (int i = 1; i < lefts.Count; i++)
            {
                if (lefts[i] <= lefts[i - 1])
                {
                    isIncreasing = false;
                    break;
                }
            }

            if (isIncreasing)
            {
                confidence = 0.85;
                return true;
            }
        }

        return false;
    }
}
