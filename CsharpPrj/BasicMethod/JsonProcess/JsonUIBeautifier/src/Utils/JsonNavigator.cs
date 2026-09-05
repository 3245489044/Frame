using Newtonsoft.Json.Linq;

namespace JsonUIBeautifier.Utils;

public static class JsonNavigator
{
    public static IEnumerable<(string Path, JToken Token)> GetAllControls(JObject root)
    {
        var views = root["views"] as JArray;
        if (views == null) yield break;

        foreach (var view in views)
        {
            var viewName = view["name"]?.ToString() ?? "unknown";
            var controls = view["controls"] as JArray;
            if (controls == null) continue;

            foreach (var control in controls)
            {
                var name = control["name"]?.ToString() ?? "unnamed";
                var path = $"views[{viewName}].controls[{name}]";
                yield return (path, control);

                foreach (var child in GetChildControls(control, path))
                    yield return child;
            }
        }
    }

    private static IEnumerable<(string Path, JToken Token)> GetChildControls(JToken parent, string parentPath)
    {
        var controls = parent["controls"] as JArray;
        if (controls == null) yield break;

        foreach (var control in controls)
        {
            var name = control["name"]?.ToString() ?? "unnamed";
            var path = $"{parentPath}.controls[{name}]";
            yield return (path, control);

            foreach (var child in GetChildControls(control, path))
                yield return child;
        }
    }

    public static IEnumerable<(string Path, JToken Token)> GetAllContainers(JObject root)
    {
        foreach (var (path, token) in GetAllControls(root))
        {
            if (token["controls"] is JArray && token["controls"]!.HasValues)
                yield return (path, token);
        }
    }

    public static IEnumerable<(string Path, JToken Token)> GetAllLeafControls(JObject root)
    {
        foreach (var (path, token) in GetAllControls(root))
        {
            if (token["controls"] == null || !token["controls"]!.HasValues)
                yield return (path, token);
        }
    }

    public static (double? Left, double? Top, double? Width, double? Height) GetLayout(JToken control)
    {
        var layout = control["layout"];
        if (layout == null) return (null, null, null, null);

        return (
            SafeGetDouble(layout["left"]),
            SafeGetDouble(layout["top"]),
            SafeGetDouble(layout["width"]),
            SafeGetDouble(layout["height"])
        );
    }

    private static double? SafeGetDouble(JToken? token)
    {
        if (token == null) return null;
        try
        {
            return token.Value<double>();
        }
        catch
        {
            return null;
        }
    }


    public static void SetLayout(JToken control, double? left, double? top, double? width, double? height)
    {
        var layout = control["layout"] as JObject;
        if (layout == null)
        {
            layout = new JObject();
            control["layout"] = layout;
        }

        if (left.HasValue) layout["left"] = Math.Round(left.Value, 3);
        if (top.HasValue) layout["top"] = Math.Round(top.Value, 3);
        if (width.HasValue) layout["width"] = Math.Round(width.Value, 3);
        if (height.HasValue) layout["height"] = Math.Round(height.Value, 3);
    }

    public static Dictionary<string, string> GetColors(JToken control)
    {
        var colors = new Dictionary<string, string>();

        if (control["backColor"]?.ToString() is { } bc && bc != "")
            colors["backColor"] = bc;
        if (control["foreColor"]?.ToString() is { } fc && fc != "")
            colors["foreColor"] = fc;
        if (control["borderColor"]?.ToString() is { } boc && boc != "")
            colors["borderColor"] = boc;

        return colors;
    }

    public static Dictionary<string, object?> GetFont(JToken control)
    {
        var font = new Dictionary<string, object?>();
        var fontToken = control["font"];
        if (fontToken == null) return font;

        if (fontToken["name"]?.ToString() is { } fn)
            font["name"] = fn;
        if (SafeGetDouble(fontToken["size"]) is { } fs)
            font["size"] = fs;
        if (fontToken["bold"]?.Type == JTokenType.Boolean)
            font["bold"] = fontToken["bold"]!.Value<bool>();

        return font;
    }


    public static string GetControlType(JToken control)
        => control["type"]?.ToString() ?? "unknown";

    public static string GetControlName(JToken control)
        => control["name"]?.ToString() ?? "unnamed";

    public static JArray? GetViews(JObject root)
        => root["views"] as JArray;

    public static JObject? GetForm(JObject root)
        => root["form"] as JObject;

    public static string BuildControlPath(JObject root, JToken target)
    {
        foreach (var (path, token) in GetAllControls(root))
        {
            if (token == target) return path;
        }
        return "unknown";
    }
}
