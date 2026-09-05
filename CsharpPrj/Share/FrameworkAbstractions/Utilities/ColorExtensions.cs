#nullable enable
using System.Drawing;

namespace FrameworkAbstractions.Utilities
{
    public static class ColorExtensions
    {
        public static Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Color.Black;

            if (!hex.StartsWith("#"))
                hex = "#" + hex;

            return ColorTranslator.FromHtml(hex);
        }

        public static string ToHex(this Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static Color Darken(this Color color, double percent)
        {
            float factor = (float)(1.0 - percent);
            return Color.FromArgb(
                color.A,
                Math.Max(0, (int)(color.R * factor)),
                Math.Max(0, (int)(color.G * factor)),
                Math.Max(0, (int)(color.B * factor)));
        }

        public static Color Lighten(this Color color, double percent)
        {
            float factor = (float)(1.0 + percent);
            return Color.FromArgb(
                color.A,
                Math.Min(255, (int)(color.R * factor)),
                Math.Min(255, (int)(color.G * factor)),
                Math.Min(255, (int)(color.B * factor)));
        }
    }
}
