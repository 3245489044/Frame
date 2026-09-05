using System.Text.RegularExpressions;

namespace JsonUIBeautifier.Utils;

public static class ColorUtils
{
    public static bool IsValidHexColor(string? color)
    {
        if (string.IsNullOrEmpty(color)) return false;
        return Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") || Regex.IsMatch(color, "^#[0-9a-fA-F]{3}$");
    }

    public static (int R, int G, int B) HexToRgb(string hex)
    {
        if (!hex.StartsWith("#"))
        {
            return NamedColorToRgb(hex);
        }

        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });

        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16)
        );
    }

    private static (int R, int G, int B) NamedColorToRgb(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "white" => (255, 255, 255),
            "black" => (0, 0, 0),
            "red" => (255, 0, 0),
            "green" => (0, 128, 0),
            "blue" => (0, 0, 255),
            "yellow" => (255, 255, 0),
            "orange" => (255, 165, 0),
            "purple" => (128, 0, 128),
            "gray" or "grey" => (128, 128, 128),
            "silver" => (192, 192, 192),
            "maroon" => (128, 0, 0),
            "navy" => (0, 0, 128),
            "teal" => (0, 128, 128),
            "olive" => (128, 128, 0),
            "lime" => (0, 255, 0),
            "aqua" or "cyan" => (0, 255, 255),
            "fuchsia" or "magenta" => (255, 0, 255),
            "transparent" => (0, 0, 0),
            _ => (0, 0, 0)
        };
    }


    public static string RgbToHex(int r, int g, int b)
        => $"#{r:X2}{g:X2}{b:X2}";

    public static double ColorDistance(string hex1, string hex2)
    {
        var (r1, g1, b1) = HexToRgb(hex1);
        var (r2, g2, b2) = HexToRgb(hex2);
        return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
    }

    public static bool AreVisuallySimilar(string hex1, string hex2, double threshold = 50)
        => ColorDistance(hex1, hex2) < threshold;

    public static bool IsLightColor(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        var luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        return luminance > 128;
    }

    public static string Lighten(string hex, double factor = 0.3)
    {
        var (r, g, b) = HexToRgb(hex);
        r = Math.Min(255, (int)(r + (255 - r) * factor));
        g = Math.Min(255, (int)(g + (255 - g) * factor));
        b = Math.Min(255, (int)(b + (255 - b) * factor));
        return RgbToHex(r, g, b);
    }

    public static string Darken(string hex, double factor = 0.3)
    {
        var (r, g, b) = HexToRgb(hex);
        r = (int)(r * (1 - factor));
        g = (int)(g * (1 - factor));
        b = (int)(b * (1 - factor));
        return RgbToHex(r, g, b);
    }

    public static List<string> GeneratePalette(string primaryColor, int count = 5)
    {
        var palette = new List<string> { primaryColor };
        for (int i = 1; i < count; i++)
        {
            var factor = 0.15 * i;
            palette.Add(Lighten(primaryColor, factor));
            palette.Add(Darken(primaryColor, factor));
        }
        return palette.Distinct().Take(count).ToList();
    }

    public static string? FindDominantColor(IEnumerable<string> colors)
    {
        return colors
            .Where(IsValidHexColor)
            .GroupBy(c => c.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
    }

    public static string NormalizeColor(string color)
        => color.StartsWith("#") ? color.ToLowerInvariant() : color;

    public static bool AreColorsSimilar(string c1, string c2)
        => NormalizeColor(c1) == NormalizeColor(c2);

    public static Dictionary<string, string> BuildColorMapping(
        IEnumerable<string> allColors,
        string? primaryColor = null,
        double threshold = 50)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uniqueColors = allColors
            .Where(IsValidHexColor)
            .Select(c => c.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (string.IsNullOrEmpty(primaryColor))
            primaryColor = FindDominantColor(uniqueColors) ?? "#000000";

        foreach (var color in uniqueColors)
        {
            if (AreVisuallySimilar(color, primaryColor, threshold))
                mapping[color] = primaryColor;
            else
                mapping[color] = color;
        }

        return mapping;
    }
}
