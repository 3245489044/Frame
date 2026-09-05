using Newtonsoft.Json.Linq;

namespace JsonUIBeautifier.Utils;

public static class ColorHelper
{
    public static List<(string Path, string Property, string Color)> CollectAllColors(JObject root)
    {
        var colorLocations = new List<(string Path, string Property, string Color)>();

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var colors = JsonNavigator.GetColors(token);
            foreach (var (prop, color) in colors)
            {
                colorLocations.Add((path, prop, color));
            }
        }

        return colorLocations;
    }

    public static Dictionary<string, int> CountColorFrequency(JObject root)
    {
        var colorMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, token) in JsonNavigator.GetAllControls(root))
        {
            var colors = JsonNavigator.GetColors(token);
            foreach (var (_, color) in colors)
            {
                var normalized = ColorUtils.NormalizeColor(color);
                if (!colorMap.ContainsKey(normalized))
                    colorMap[normalized] = 0;
                colorMap[normalized]++;
            }
        }

        return colorMap;
    }

    public static Dictionary<string, JToken> BuildPathTokenMap(JObject root)
    {
        var map = new Dictionary<string, JToken>();
        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            map[path] = token;
        }
        return map;
    }
}
