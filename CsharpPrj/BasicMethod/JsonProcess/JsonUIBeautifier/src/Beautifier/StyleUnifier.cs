using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Beautifier;

public class StyleUnifier
{
    private readonly BeautifierConfig _config;

    public StyleUnifier(BeautifierConfig config)
    {
        _config = config;
    }

    public List<BeautifyChange> Unify(JObject root)
    {
        var changes = new List<BeautifyChange>();

        var typeStyles = new Dictionary<string, List<(string Path, JToken Token)>>();

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var type = JsonNavigator.GetControlType(token);
            if (!typeStyles.ContainsKey(type))
                typeStyles[type] = new();
            typeStyles[type].Add((path, token));
        }

        foreach (var (type, controls) in typeStyles)
        {
            if (controls.Count < 2) continue;

            var bgChanges = UnifyProperty(type, controls, "backColor", root);
            changes.AddRange(bgChanges);

            var fgChanges = UnifyProperty(type, controls, "foreColor", root);
            changes.AddRange(fgChanges);

            var fontChanges = UnifyFont(type, controls, root);
            changes.AddRange(fontChanges);
        }

        return changes;
    }

    private List<BeautifyChange> UnifyProperty(
        string type, List<(string Path, JToken Token)> controls, string property, JObject root)
    {
        var changes = new List<BeautifyChange>();

        var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, token) in controls)
        {
            var val = token[property]?.ToString();
            if (!string.IsNullOrEmpty(val))
            {
                var normalized = ColorUtils.NormalizeColor(val);
                if (!valueCounts.ContainsKey(normalized))
                    valueCounts[normalized] = 0;
                valueCounts[normalized]++;
            }
        }

        if (valueCounts.Count <= 1) return changes;

        var dominantValue = valueCounts.OrderByDescending(v => v.Value).First().Key;

        foreach (var (path, token) in controls)
        {
            var currentVal = token[property]?.ToString();
            if (!string.IsNullOrEmpty(currentVal) && !ColorUtils.AreColorsSimilar(currentVal, dominantValue))
            {
                var before = token[property]?.ToString();
                token[property] = dominantValue;
                changes.Add(new BeautifyChange
                {
                    Type = "style_unified",
                    Target = $"{path}.{property}",
                    Before = before,
                    After = dominantValue,
                    Description = $"统一 {type} 的 {property}: {before} → {dominantValue}"
                });
            }
        }

        return changes;
    }

    private List<BeautifyChange> UnifyFont(
        string type, List<(string Path, JToken Token)> controls, JObject root)
    {
        var changes = new List<BeautifyChange>();

        var fontCounts = new Dictionary<string, int>();
        foreach (var (_, token) in controls)
        {
            var font = token["font"];
            if (font != null)
            {
                var key = font.ToString();
                if (!fontCounts.ContainsKey(key))
                    fontCounts[key] = 0;
                fontCounts[key]++;
            }
        }

        if (fontCounts.Count <= 1) return changes;

        var dominantFont = fontCounts.OrderByDescending(f => f.Value).First().Key;

        foreach (var (path, token) in controls)
        {
            var currentFont = token["font"]?.ToString();
            if (currentFont != null && currentFont != dominantFont)
            {
                var before = token["font"]?.ToString();
                token["font"] = JObject.Parse(dominantFont);
                changes.Add(new BeautifyChange
                {
                    Type = "style_unified",
                    Target = $"{path}.font",
                    Before = before,
                    After = dominantFont,
                    Description = $"统一 {type} 的字体配置"
                });
            }
        }

        return changes;
    }
}
