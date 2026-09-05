using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;
using JsonUIBeautifier.Stylizer.Models;
using JsonUIBeautifier.Stylizer.Resolvers;

namespace JsonUIBeautifier.Stylizer.Applicators;

public class TypographyApplicator
{
    private readonly StyleDefinition _style;

    public TypographyApplicator(StyleDefinition style)
    {
        _style = style;
    }

    public List<BeautifyChange> ApplyTypography(JObject root)
    {
        var changes = new List<BeautifyChange>();
        var typography = _style.Typography;

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var type = JsonNavigator.GetControlType(token);
            var textRole = ControlRoleResolver.InferTextRole(type, token);
            var (fontSize, isBold) = ColorResolver.ResolveFontSize(textRole, typography);

            var fontFamily = ControlRoleResolver.IsCodeInput(token)
                ? typography.FontFamilyMono
                : typography.FontFamily;

            var font = token["font"] as JObject ?? new JObject();
            var beforeFont = font.ToString();

            font["name"] = fontFamily;
            font["size"] = fontSize;

            if (isBold)
                font["bold"] = true;
            else
                font.Remove("bold");

            token["font"] = font;

            var afterFont = font.ToString();
            if (beforeFont != afterFont)
            {
                changes.Add(new BeautifyChange
                {
                    Type = "style_typography_applied",
                    Target = $"{path}.font",
                    Before = beforeFont,
                    After = afterFont,
                    Description = $"应用字体系统 [{textRole}]: {fontFamily} {fontSize}pt{(isBold ? " Bold" : "")}"
                });
            }
        }

        return changes;
    }
}
