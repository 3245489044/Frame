using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Models;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Beautifier;

public class LayoutOptimizer
{
    private readonly BeautifierConfig _config;

    public LayoutOptimizer(BeautifierConfig config)
    {
        _config = config;
    }

    public List<BeautifyChange> Optimize(JObject root)
    {
        var changes = new List<BeautifyChange>();

        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            var (left, top, width, height) = JsonNavigator.GetLayout(token);
            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue)
                continue;

            var (newLeft, newTop, newWidth, newHeight) = LayoutMath.AlignLayout(
                left.Value, top.Value, width.Value, height.Value, _config.GridAlignment);


            bool changed = false;
            if (Math.Abs(newLeft - left.Value) > 0.001) changed = true;
            if (Math.Abs(newTop - top.Value) > 0.001) changed = true;
            if (Math.Abs(newWidth - width.Value) > 0.001) changed = true;
            if (Math.Abs(newHeight - height.Value) > 0.001) changed = true;

            if (changed)
            {
                JsonNavigator.SetLayout(token, newLeft, newTop, newWidth, newHeight);
                changes.Add(new BeautifyChange
                {
                    Type = "layout_optimized",
                    Target = path,
                    Before = $"{{left:{left.Value:F3},top:{top.Value:F3},width:{width.Value:F3},height:{height.Value:F3}}}",
                    After = $"{{left:{newLeft:F3},top:{newTop:F3},width:{newWidth:F3},height:{newHeight:F3}}}",
                    Description = $"布局对齐到 {_config.GridAlignment} 网格"
                });
            }
        }

        return changes;
    }
}
