using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Refactor;

public class ThemeExtractor
{
    private readonly BeautifierConfig _config;

    private static readonly (string JsonKey, string Description)[] ThemePropertyMappings =
    {
        ("primaryColor", "提取主色: {0}"),
        ("secondaryColor", "提取辅色: {0}"),
        ("surfaceColor", "提取表面色: {0}"),
        ("textPrimary", "提取主文字色: {0}"),
        ("textSecondary", "提取次文字色: {0}"),
        ("fontFamily", "提取字体: {0}"),
    };

    public ThemeExtractor(BeautifierConfig config)
    {
        _config = config;
    }

    public (List<BeautifyChange> Changes, ExtractedTheme? Theme) Extract(JObject root)
    {
        var changes = new List<BeautifyChange>();

        var styleAnalyzer = new Analyzer.StyleAnalyzer();
        var theme = styleAnalyzer.ExtractThemeCandidate(root);

        if (theme == null) return (changes, null);

        var themeObj = new JObject();

        var themeType = typeof(ExtractedTheme);
        foreach (var (jsonKey, description) in ThemePropertyMappings)
        {
            var prop = themeType.GetProperty(
                jsonKey[0].ToString().ToUpper() + jsonKey[1..]);
            if (prop == null) continue;

            var value = prop.GetValue(theme);
            if (value is string strVal && strVal != null)
            {
                themeObj[jsonKey] = strVal;
                changes.Add(new BeautifyChange
                {
                    Type = "theme_extracted",
                    Target = $"theme.{jsonKey}",
                    After = strVal,
                    Description = string.Format(description, strVal)
                });
            }
        }

        if (theme.FontSize.HasValue)
        {
            themeObj["fontSize"] = theme.FontSize.Value;
            changes.Add(new BeautifyChange
            {
                Type = "theme_extracted",
                Target = "theme.fontSize",
                After = theme.FontSize.ToString(),
                Description = $"提取字号: {theme.FontSize}"
            });
        }

        if (themeObj.HasValues)
        {
            root["theme"] = themeObj;
        }

        return (changes, theme);
    }
}


