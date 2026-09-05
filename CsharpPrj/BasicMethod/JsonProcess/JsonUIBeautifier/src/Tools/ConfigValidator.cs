using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Utils;

namespace JsonUIBeautifier.Tools;

public static class ConfigValidator
{
    public class ValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public static ValidationResult Validate(string json)
    {
        var result = new ValidationResult();

        try
        {
            var root = JObject.Parse(json);

            ValidateForm(root, result);

            ValidateViews(root, result);

            ValidateControls(root, result);
        }
        catch (Newtonsoft.Json.JsonReaderException ex)
        {
            result.IsValid = false;
            result.Errors.Add($"JSON 格式错误: {ex.Message}");
        }

        return result;
    }

    private static void ValidateForm(JObject root, ValidationResult result)
    {
        var form = root["form"];
        if (form == null)
        {
            result.Warnings.Add("缺少 form 配置节");
            return;
        }

        if (form["title"] == null)
            result.Warnings.Add("form 缺少 title 字段");

        if (form["width"] == null)
            result.Warnings.Add("form 缺少 width 字段");

        if (form["height"] == null)
            result.Warnings.Add("form 缺少 height 字段");
    }

    private static void ValidateViews(JObject root, ValidationResult result)
    {
        var views = root["views"] as JArray;
        if (views == null || views.Count == 0)
        {
            result.Warnings.Add("缺少 views 配置或 views 为空");
            return;
        }

        foreach (var view in views)
        {
            if (view["name"] == null)
                result.Errors.Add("view 缺少 name 字段");

            if (view["controls"] == null)
                result.Warnings.Add($"view '{view["name"]}' 缺少 controls 字段");
        }
    }

    private static void ValidateControls(JObject root, ValidationResult result)
    {
        foreach (var (path, token) in JsonNavigator.GetAllControls(root))
        {
            if (token["type"] == null)
                result.Errors.Add($"{path}: 缺少 type 字段");

            var layout = token["layout"];
            if (layout != null)
            {
                ValidateLayout(path, layout, result);
            }

            ValidateColors(path, token, result);
        }
    }

    private static void ValidateLayout(string path, JToken layout, ValidationResult result)
    {
        var props = new[] { "left", "top", "width", "height" };
        foreach (var prop in props)
        {
            if (layout[prop] == null)
                result.Warnings.Add($"{path}.layout: 缺少 {prop} 字段");
            else if (layout[prop]?.Type != JTokenType.Float && layout[prop]?.Type != JTokenType.Integer)
                result.Errors.Add($"{path}.layout.{prop}: 应为数值类型");
        }
    }

    private static void ValidateColors(string path, JToken token, ValidationResult result)
    {
        var colorProps = new[] { "backColor", "foreColor", "borderColor" };
        foreach (var prop in colorProps)
        {
            var val = token[prop]?.ToString();
            if (!string.IsNullOrEmpty(val) && !Utils.ColorUtils.IsValidHexColor(val))
            {
                result.Warnings.Add($"{path}.{prop}: '{val}' 不是有效的十六进制颜色");
            }
        }
    }

    public static ValidationResult ValidateFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return Validate(json);
    }
}
