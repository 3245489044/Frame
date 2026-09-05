using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;
using JsonUIBeautifier.Stylizer.Models;
using JsonUIBeautifier.Stylizer.Resolvers;

namespace JsonUIBeautifier.Stylizer.Applicators;

public class BorderApplicator
{
    private readonly StyleDefinition _style;

    public BorderApplicator(StyleDefinition style)
    {
        _style = style;
    }

    public List<BeautifyChange> ApplyBorders(JObject root)
    {
        var changes = new List<BeautifyChange>();
        var borders = _style.Borders;

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var type = JsonNavigator.GetControlType(token);
            var radius = ColorResolver.ResolveBorderRadius(type, borders);

            if (radius > 0)
            {
                var before = token["borderRadius"]?.ToString();
                token["borderRadius"] = radius;
                if (before != radius.ToString())
                {
                    changes.Add(new BeautifyChange
                    {
                        Type = "style_border_applied",
                        Target = $"{path}.borderRadius",
                        Before = before,
                        After = radius.ToString(),
                        Description = $"应用圆角: {radius}px"
                    });
                }
            }

            var borderWidth = type.ToLowerInvariant() switch
            {
                "textbox" or "richtextbox" or "combobox" => borders.WidthDefault,
                "panel" or "groupbox" => 0,
                "datagridview" or "listview" or "datagrid" => borders.WidthDefault,
                _ => 0,
            };

            if (borderWidth > 0)
            {
                var before = token["borderWidth"]?.ToString();
                token["borderWidth"] = borderWidth;
                if (before != borderWidth.ToString())
                {
                    changes.Add(new BeautifyChange
                    {
                        Type = "style_border_applied",
                        Target = $"{path}.borderWidth",
                        Before = before,
                        After = borderWidth.ToString(),
                        Description = $"应用边框宽度: {borderWidth}px"
                    });
                }
            }
        }

        return changes;
    }
}
