
using System.Drawing;
using System.Globalization;

namespace Modules.Common.Drawing
{
    public static class ColorExtensions
    {
        #region 颜色解析

        public static Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                throw new ArgumentException("颜色字符串不能为空", nameof(hex));

            hex = hex.TrimStart('#');

            if (hex.Length == 6)
            {
                int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromArgb(r, g, b);
            }
            else if (hex.Length == 8)
            {
                int a = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                int r = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }

            throw new ArgumentException($"无法解析颜色字符串: {hex}", nameof(hex));
        }

        public static Color FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("颜色名称不能为空", nameof(name));

            var color = Color.FromName(name);
            if (!color.IsKnownColor && !color.IsNamedColor && color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
            {
                throw new ArgumentException($"未知的颜色名称: {name}", nameof(name));
            }
            return color;
        }

        public static bool TryParse(string? value, out Color color)
        {
            color = Color.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.StartsWith("#") || value.Length == 6 || value.Length == 8)
            {
                try
                {
                    color = FromHex(value);
                    return true;
                }
                catch
                {
                }
            }

            try
            {
                color = FromName(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 颜色转换

        public static string ToHex(this Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static string ToHexWithAlpha(this Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static string ToArgbString(this Color color)
        {
            return $"{color.A}, {color.R}, {color.G}, {color.B}";
        }

        #endregion

        #region 颜色调整

        public static Color Lighten(this Color color, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);

            int r = (int)(color.R + (255 - color.R) * factor);
            int g = (int)(color.G + (255 - color.G) * factor);
            int b = (int)(color.B + (255 - color.B) * factor);

            return Color.FromArgb(color.A, r, g, b);
        }

        public static Color Darken(this Color color, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);

            int r = (int)(color.R * (1 - factor));
            int g = (int)(color.G * (1 - factor));
            int b = (int)(color.B * (1 - factor));

            return Color.FromArgb(color.A, r, g, b);
        }

        public static Color Blend(this Color color1, Color color2, float ratio)
        {
            ratio = Math.Clamp(ratio, 0f, 1f);

            int a = (int)(color1.A + (color2.A - color1.A) * ratio);
            int r = (int)(color1.R + (color2.R - color1.R) * ratio);
            int g = (int)(color1.G + (color2.G - color1.G) * ratio);
            int b = (int)(color1.B + (color2.B - color1.B) * ratio);

            return Color.FromArgb(a, r, g, b);
        }

        public static Color WithAlpha(this Color color, int alpha)
        {
            alpha = Math.Clamp(alpha, 0, 255);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static Color WithAlphaPercent(this Color color, float alphaPercent)
        {
            alphaPercent = Math.Clamp(alphaPercent, 0f, 1f);
            return Color.FromArgb((int)(alphaPercent * 255), color.R, color.G, color.B);
        }

        #endregion

        #region 颜色对比度

        public static double GetRelativeLuminance(this Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        public static double GetContrastRatio(this Color color1, Color color2)
        {
            double l1 = color1.GetRelativeLuminance();
            double l2 = color2.GetRelativeLuminance();

            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        public static bool IsLight(this Color color)
        {
            double perceivedBrightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return perceivedBrightness > 0.5;
        }

        public static bool IsDark(this Color color)
        {
            return !color.IsLight();
        }

        public static Color GetContrastColor(this Color color)
        {
            double whiteContrast = color.GetContrastRatio(Color.White);
            double blackContrast = color.GetContrastRatio(Color.Black);

            return whiteContrast >= blackContrast ? Color.White : Color.Black;
        }

        public static Color GetAccessibleContrastColor(this Color color)
        {
            double whiteContrast = color.GetContrastRatio(Color.White);

            if (whiteContrast >= 4.5)
            {
                return Color.White;
            }

            return Color.Black;
        }

        #endregion

        #region 预定义颜色快捷方式

        public static Color SystemAccent => SystemColors.Highlight;

        public static Color SystemAccentText => SystemColors.HighlightText;

        public static Color WindowBackground => SystemColors.Window;

        public static Color ControlBackground => SystemColors.Control;

        public static Color InfoBackground => SystemColors.Info;

        #endregion
    }
}
