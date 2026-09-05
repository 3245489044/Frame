using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Refactor;

public class StyleDeduplicator
{
    private readonly BeautifierConfig _config;

    public StyleDeduplicator(BeautifierConfig config)
    {
        _config = config;
    }

    public List<BeautifyChange> Deduplicate(JObject root)
    {
        var changes = new List<BeautifyChange>();

        var styleGroups = new Dictionary<string, List<(string Path, JToken Token)>>();

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var styleKey = BuildStyleKey(token);
            if (styleKey == null) continue;

            if (!styleGroups.ContainsKey(styleKey))
                styleGroups[styleKey] = new();
            styleGroups[styleKey].Add((path, token));
        }

        var duplicateStyles = styleGroups
            .Where(g => g.Value.Count >= 3)
            .OrderByDescending(g => g.Value.Count)
            .ToList();

        if (duplicateStyles.Count == 0) return changes;

        var stylesObj = new JObject();
        var styleIndex = 0;

        foreach (var (styleKey, controls) in duplicateStyles)
        {
            var styleName = $"sharedStyle_{styleIndex++}";
            var styleDef = ParseStyleKey(styleKey);

            if (styleDef != null)
            {
                stylesObj[styleName] = styleDef;

                foreach (var (path, token) in controls)
                {
                    token["styleRef"] = styleName;

                    foreach (var prop in new[] { "backColor", "foreColor", "borderColor", "font" })
                    {
                        if (token[prop] != null)
                        {
                            changes.Add(new BeautifyChange
                            {
                                Type = "style_deduplicated",
                                Target = $"{path}.{prop}",
                                Before = token[prop]?.ToString(),
                                After = $"{{styleRef:{styleName}}}",
                                Description = $"样式提取到 {styleName}"
                            });
                            token[prop]?.Parent?.Remove();
                        }
                    }
                }
            }
        }

        if (stylesObj.HasValues)
        {
            root["styles"] = stylesObj;
            changes.Add(new BeautifyChange
            {
                Type = "style_deduplicated",
                Target = "styles",
                After = stylesObj.ToString(),
                Description = $"提取了 {duplicateStyles.Count} 个共享样式"
            });
        }

        return changes;
    }

    private string? BuildStyleKey(JToken token)
    {
        var parts = new List<string>();

        if (token["backColor"]?.ToString() is { } bc && bc != "")
            parts.Add($"bg:{bc}");
        if (token["foreColor"]?.ToString() is { } fc && fc != "")
            parts.Add($"fg:{fc}");
        if (token["borderColor"]?.ToString() is { } boc && boc != "")
            parts.Add($"bo:{boc}");
        if (token["font"] is JObject font)
            parts.Add($"font:{font.ToString(Newtonsoft.Json.Formatting.None)}");

        return parts.Count > 0 ? string.Join("|", parts) : null;
    }

    private JObject? ParseStyleKey(string styleKey)
    {
        var obj = new JObject();
        var parts = styleKey.Split('|');

        foreach (var part in parts)
        {
            if (part.StartsWith("bg:"))
                obj["backColor"] = part[3..];
            else if (part.StartsWith("fg:"))
                obj["foreColor"] = part[3..];
            else if (part.StartsWith("bo:"))
                obj["borderColor"] = part[3..];
            else if (part.StartsWith("font:"))
            {
                try
                {
                    obj["font"] = JObject.Parse(part[5..]);
                }
                catch { }
            }
        }

        return obj.HasValues ? obj : null;
    }
}
