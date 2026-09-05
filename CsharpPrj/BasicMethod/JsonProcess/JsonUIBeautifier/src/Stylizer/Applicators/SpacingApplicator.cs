using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;
using JsonUIBeautifier.Stylizer.Models;

namespace JsonUIBeautifier.Stylizer.Applicators;

public class SpacingApplicator
{
    private readonly StyleDefinition _style;

    public SpacingApplicator(StyleDefinition style)
    {
        _style = style;
    }

    public List<BeautifyChange> ApplySpacing(JObject root)
    {
        var changes = new List<BeautifyChange>();
        var spacing = _style.Spacing;
        var unit = spacing.Unit / 1000.0;

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var (l, t, w, h) = JsonNavigator.GetLayout(token);
            if (!l.HasValue) continue;

            var newL = SnapToUnit(l.Value, unit);
            var newT = SnapToUnit(t.Value, unit);
            var newW = w.HasValue ? SnapToUnit(w.Value, unit) : w;
            var newH = h.HasValue ? SnapToUnit(h.Value, unit) : h;

            var before = $"{{left:{l.Value:F3},top:{t.Value:F3},width:{w:F3},height:{h:F3}}}";
            JsonNavigator.SetLayout(token, newL, newT, newW, newH);
            var after = $"{{left:{newL:F3},top:{newT:F3},width:{newW:F3},height:{newH:F3}}}";

            if (before != after)
            {
                changes.Add(new BeautifyChange
                {
                    Type = "style_spacing_applied",
                    Target = $"{path}.layout",
                    Before = before,
                    After = after,
                    Description = $"对齐到 {spacing.Unit}px 间距网格"
                });
            }
        }

        foreach (var (path, container) in JsonNavigator.GetAllContainers(root))
        {
            var paddingRelative = spacing.PaddingMd / 1000.0;
            container["padding"] = Math.Round(paddingRelative, 3);

            changes.Add(new BeautifyChange
            {
                Type = "style_spacing_applied",
                Target = $"{path}.padding",
                After = paddingRelative.ToString("F3"),
                Description = $"应用容器内边距: {spacing.PaddingMd}px"
            });
        }

        return changes;
    }

    private static double SnapToUnit(double value, double unit)
    {
        if (unit <= 0) return value;
        return Math.Round(value / unit) * unit;
    }
}
