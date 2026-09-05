using Newtonsoft.Json.Linq;

namespace JsonUIBeautifier.Utils;

public static class LayoutHelper
{
    public static List<(string Path, JToken Token, double L, double T, double W, double H)> GetAllLayouts(JObject root)
    {
        return JsonNavigator.GetAllControls(root)
            .Select(c => (c.Path, c.Token, Layout: JsonNavigator.GetLayout(c.Token)))
            .Where(c => c.Layout.Left.HasValue && c.Layout.Top.HasValue &&
                        c.Layout.Width.HasValue && c.Layout.Height.HasValue)
            .Select(c => (c.Path, c.Token, c.Layout.Left!.Value, c.Layout.Top!.Value, c.Layout.Width!.Value, c.Layout.Height!.Value))
            .ToList();
    }

    public static List<(double L, double T, double W, double H)> GetLayoutValues(JObject root)
    {
        return GetAllLayouts(root)
            .Select(c => (c.L, c.T, c.W, c.H))
            .ToList();
    }
}
