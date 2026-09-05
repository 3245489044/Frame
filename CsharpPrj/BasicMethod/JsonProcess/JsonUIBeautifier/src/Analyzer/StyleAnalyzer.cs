using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Analyzer;

public class StyleAnalyzer
{
    public List<ColorUsage> AnalyzeColors(JObject root)
    {
        var colorMap = new Dictionary<string, ColorUsage>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, prop, color) in ColorHelper.CollectAllColors(root))
        {
            var normalized = ColorUtils.NormalizeColor(color);
            if (!colorMap.ContainsKey(normalized))
            {
                colorMap[normalized] = new ColorUsage
                {
                    Color = normalized,
                    Count = 0,
                    UsedBy = new List<string>()
                };
            }
            colorMap[normalized].Count++;
            colorMap[normalized].UsedBy.Add($"{path}.{prop}");
        }

        return colorMap.Values
            .OrderByDescending(c => c.Count)
            .ToList();
    }


    public Dictionary<string, int> AnalyzeFonts(JObject root)
    {
        var fontMap = new Dictionary<string, int>();

        foreach (var (_, token) in JsonNavigator.GetAllControls(root))
        {
            var font = JsonNavigator.GetFont(token);
            if (font.Count > 0)
            {
                var key = string.Join("|", font.Select(kv => $"{kv.Key}={kv.Value}"));
                if (!fontMap.ContainsKey(key))
                    fontMap[key] = 0;
                fontMap[key]++;
            }
        }

        return fontMap;
    }

    public double EvaluateColorConsistency(List<ColorUsage> colorUsages)
    {
        if (colorUsages.Count == 0) return 10.0;

        double score = 10.0;

        if (colorUsages.Count > 8) score -= 3.0;
        else if (colorUsages.Count > 5) score -= 1.5;
        else if (colorUsages.Count > 3) score -= 0.5;

        var similarPairs = 0;
        for (int i = 0; i < colorUsages.Count; i++)
        {
            for (int j = i + 1; j < colorUsages.Count; j++)
            {
                if (ColorUtils.AreVisuallySimilar(colorUsages[i].Color, colorUsages[j].Color, 40))
                    similarPairs++;
            }
        }
        score -= similarPairs * 0.5;

        return Math.Round(Math.Max(0, score), 1);
    }

    public ExtractedTheme? ExtractThemeCandidate(JObject root)
    {
        var colors = AnalyzeColors(root);
        if (colors.Count == 0) return null;

        var theme = new ExtractedTheme();

        var dominant = colors.FirstOrDefault();
        if (dominant != null)
        {
            theme.PrimaryColor = dominant.Color;
            theme.SecondaryColor = colors.Count > 1 ? colors[1].Color : null;
        }

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var type = JsonNavigator.GetControlType(token);
            var controlColors = JsonNavigator.GetColors(token);

            if (controlColors.TryGetValue("backColor", out var bg))
            {
                if (type is "label" or "textBox" or "richTextBox")
                    theme.SurfaceColor ??= bg;
            }

            if (controlColors.TryGetValue("foreColor", out var fg))
            {
                if (type is "label" or "textBox")
                {
                    theme.TextPrimary ??= fg;
                    theme.TextSecondary ??= fg;
                }
            }
        }

        foreach (var (_, token) in JsonNavigator.GetAllControls(root))
        {
            var font = JsonNavigator.GetFont(token);
            if (font.TryGetValue("name", out var name) && name is string fn)
                theme.FontFamily ??= fn;
            if (font.TryGetValue("size", out var size) && size is double fs)
                theme.FontSize ??= fs;
        }

        return theme;
    }
}
