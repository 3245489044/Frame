#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ECS.Core;
using TConfig.ECS.Components;

namespace FrontEndDriven.Validation.Validators;

public class StyleValidator : IEntityValidator
{
    private static readonly HashSet<string> KnownFontFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arial", "Calibri", "Cambria", "Candara", "Comic Sans MS", "Consolas",
        "Courier New", "David", "Franklin Gothic Medium", "Georgia", "Impact",
        "Lucida Console", "Lucida Sans Unicode", "Microsoft Sans Serif",
        "Microsoft YaHei", "MingLiU", "Palatino Linotype", "Segoe UI",
        "SimHei", "SimSun", "Tahoma", "Times New Roman", "Trebuchet MS",
        "Verdana", "Webdings", "Wingdings", "宋体", "微软雅黑", "黑体", "楷体"
    };

    private static readonly HashSet<string> WinFormsSystemColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActiveBorder", "ActiveCaption", "ActiveCaptionText", "AppWorkspace",
        "ButtonFace", "ButtonHighlight", "ButtonShadow",
        "Control", "ControlDark", "ControlDarkDark", "ControlLight",
        "ControlLightLight", "ControlText",
        "Desktop", "GradientActiveCaption", "GradientInactiveCaption",
        "GrayText", "Highlight", "HighlightText", "HotTrack",
        "InactiveBorder", "InactiveCaption", "InactiveCaptionText",
        "Info", "InfoText", "Menu", "MenuBar", "MenuHighlight",
        "MenuText", "ScrollBar", "Window", "WindowFrame", "WindowText"
    };

    private static readonly HashSet<string> KnownNamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure",
        "Beige", "Bisque", "Black", "BlanchedAlmond", "Blue",
        "BlueViolet", "Brown", "BurlyWood", "CadetBlue", "Chartreuse",
        "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson",
        "Cyan", "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray",
        "DarkGreen", "DarkKhaki", "DarkMagenta", "DarkOliveGreen", "DarkOrange",
        "DarkOrchid", "DarkRed", "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue",
        "DarkSlateGray", "DarkTurquoise", "DarkViolet", "DeepPink", "DeepSkyBlue",
        "DimGray", "DodgerBlue", "Firebrick", "FloralWhite", "ForestGreen",
        "Fuchsia", "Gainsboro", "GhostWhite", "Gold", "Goldenrod",
        "Gray", "Green", "GreenYellow", "Honeydew", "HotPink",
        "IndianRed", "Indigo", "Ivory", "Khaki", "Lavender",
        "LavenderBlush", "LawnGreen", "LemonChiffon", "LightBlue", "LightCoral",
        "LightCyan", "LightGoldenrodYellow", "LightGray", "LightGreen", "LightPink",
        "LightSalmon", "LightSeaGreen", "LightSkyBlue", "LightSlateGray", "LightSteelBlue",
        "LightYellow", "Lime", "LimeGreen", "Linen", "Magenta",
        "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid", "MediumPurple",
        "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen", "MediumTurquoise", "MediumVioletRed",
        "MidnightBlue", "MintCream", "MistyRose", "Moccasin", "NavajoWhite",
        "Navy", "OldLace", "Olive", "OliveDrab", "Orange",
        "OrangeRed", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise",
        "PaleVioletRed", "PapayaWhip", "PeachPuff", "Peru", "Pink",
        "Plum", "PowderBlue", "Purple", "Red", "RosyBrown",
        "RoyalBlue", "SaddleBrown", "Salmon", "SandyBrown", "SeaGreen",
        "SeaShell", "Sienna", "Silver", "SkyBlue", "SlateBlue",
        "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan",
        "Teal", "Thistle", "Tomato", "Transparent", "Turquoise",
        "Violet", "Wheat", "White", "WhiteSmoke", "Yellow", "YellowGreen"
    };

    private static readonly Regex HexColorRegex = new(
        @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
        RegexOptions.Compiled);

    public string ValidatorName => "Style";

    public ValidationCategory Category => ValidationCategory.Style;

    public void Validate(Entity entity, EntityManager entityManager,
                         ValidationContext context, ValidationResult result)
    {
        if (!entityManager.TryGetComponent<MergedStyleComponent>(entity, out var style))
            return;

        var entityName = EntityValidationHelper.GetEntityName(entity, entityManager);

        if (string.IsNullOrWhiteSpace(style.FontFamily))
        {
            result.AddWarning(new ValidationError(
                entity, entityName, Category,
                "MergedStyleComponent.FontFamily 为空",
                ValidationSeverity.Warning,
                nameof(MergedStyleComponent)));
        }
        else if (!KnownFontFamilies.Contains(style.FontFamily))
        {
            result.AddWarning(new ValidationError(
                entity, entityName, Category,
                $"MergedStyleComponent.FontFamily = \"{style.FontFamily}\" 不是已知的字体名（可能为自定义字体）",
                ValidationSeverity.Warning,
                nameof(MergedStyleComponent)));
        }

        if (style.FontSize <= 0)
        {
            result.AddError(new ValidationError(
                entity, entityName, Category,
                $"MergedStyleComponent.FontSize = {style.FontSize}，必须为正数",
                ValidationSeverity.Error,
                nameof(MergedStyleComponent)));
        }

        CheckColor(entity, entityName, "ForeColor", style.ForeColor, result, Category);
        CheckColor(entity, entityName, "BackColor", style.BackColor, result, Category);
        CheckColor(entity, entityName, "BorderColor", style.BorderColor, result, Category);

        if (style.BorderWidth < 0)
        {
            result.AddError(new ValidationError(
                entity, entityName, Category,
                $"MergedStyleComponent.BorderWidth = {style.BorderWidth}，不能为负数",
                ValidationSeverity.Error,
                nameof(MergedStyleComponent)));
        }
    }

    private static void CheckColor(
        Entity entity, string entityName, string fieldName, string color,
        ValidationResult result, ValidationCategory category)
    {
        if (string.IsNullOrWhiteSpace(color))
            return;

        if (HexColorRegex.IsMatch(color))
            return;

        if (KnownNamedColors.Contains(color))
            return;

        if (int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return;

        if (WinFormsSystemColors.Contains(color))
            return;

        result.AddWarning(new ValidationError(
            entity, entityName, category,
            $"MergedStyleComponent.{fieldName} = \"{color}\" 不是有效的颜色格式（应为 #RRGGBB、#RGB 或已知颜色名）",
            ValidationSeverity.Warning,
            nameof(MergedStyleComponent)));
    }
}