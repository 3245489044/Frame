using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Beautifier;

public class ColorHarmonizer
{
    private readonly BeautifierConfig _config;

    public ColorHarmonizer(BeautifierConfig config)
    {
        _config = config;
    }

    public (List<BeautifyChange> Changes, string? PrimaryColor) Harmonize(JObject root)
    {
        var changes = new List<BeautifyChange>();

        var colorLocations = ColorHelper.CollectAllColors(root);
        if (colorLocations.Count == 0) return (changes, null);

        var allColors = colorLocations.Select(c => c.Color).ToList();

        var primaryColor = _config.PrimaryColor ?? ColorUtils.FindDominantColor(allColors);
        if (primaryColor == null) return (changes, null);

        var mapping = ColorUtils.BuildColorMapping(allColors, primaryColor, 50);

        var pathTokenMap = ColorHelper.BuildPathTokenMap(root);

        foreach (var (path, prop, color) in colorLocations)
        {
            var normalized = ColorUtils.NormalizeColor(color);
            if (mapping.TryGetValue(normalized, out var mapped) &&
                !ColorUtils.AreColorsSimilar(normalized, mapped) &&
                pathTokenMap.TryGetValue(path, out var token))
            {
                var before = token[prop]?.ToString();
                token[prop] = mapped;
                changes.Add(new BeautifyChange
                {
                    Type = "color_unified",
                    Target = $"{path}.{prop}",
                    Before = before,
                    After = mapped,
                    Description = $"颜色统一: {before} → {mapped}"
                });
            }
        }

        return (changes, primaryColor);
    }
}


