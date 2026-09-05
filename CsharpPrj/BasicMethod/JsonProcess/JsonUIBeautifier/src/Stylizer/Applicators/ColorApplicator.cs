using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;
using JsonUIBeautifier.Stylizer.Models;
using JsonUIBeautifier.Stylizer.Resolvers;

namespace JsonUIBeautifier.Stylizer.Applicators;

public class ColorApplicator
{
    private readonly StyleDefinition _style;

    public ColorApplicator(StyleDefinition style)
    {
        _style = style;
    }

    public List<BeautifyChange> ApplyPalette(JObject root)
    {
        var changes = new List<BeautifyChange>();
        var palette = _style.Colors;

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var type = JsonNavigator.GetControlType(token);
            var role = ControlRoleResolver.InferControlRole(type, token, root);
            var (bg, fg, border) = ColorResolver.ResolveColors(role, palette);

            if (bg != null && ShouldApplyBackColor(role, token))
            {
                var before = token["backColor"]?.ToString();
                token["backColor"] = bg;
                if (before != bg)
                {
                    changes.Add(new BeautifyChange
                    {
                        Type = "style_color_applied",
                        Target = $"{path}.backColor",
                        Before = before,
                        After = bg,
                        Description = $"应用风格色板 [{role}]: {bg}"
                    });
                }
            }

            if (fg != null)
            {
                var before = token["foreColor"]?.ToString();
                token["foreColor"] = fg;
                if (before != fg)
                {
                    changes.Add(new BeautifyChange
                    {
                        Type = "style_color_applied",
                        Target = $"{path}.foreColor",
                        Before = before,
                        After = fg,
                        Description = $"应用风格色板 [{role}]: {fg}"
                    });
                }
            }

            if (border != null)
            {
                var before = token["borderColor"]?.ToString();
                token["borderColor"] = border;
                if (before != border)
                {
                    changes.Add(new BeautifyChange
                    {
                        Type = "style_color_applied",
                        Target = $"{path}.borderColor",
                        Before = before,
                        After = border,
                        Description = $"应用风格色板 [{role}]: {border}"
                    });
                }
            }
        }

        var themeObj = new JObject();
        foreach (var (key, value) in ColorResolver.ConvertPaletteToTheme(palette))
        {
            if (value != null)
                themeObj[key] = JToken.FromObject(value);
        }
        root["theme"] = themeObj;

        changes.Add(new BeautifyChange
        {
            Type = "style_theme_injected",
            Target = "theme",
            After = themeObj.ToString(),
            Description = $"注入风格主题: {_style.Name}"
        });

        return changes;
    }

    private bool ShouldApplyBackColor(ControlRole role, JToken token)
    {
        if (role is ControlRole.Container or ControlRole.Input or ControlRole.DataTable)
            return true;

        if (role is ControlRole.PrimaryButton or ControlRole.SecondaryButton)
            return true;

        return token["backColor"] != null;
    }
}
