using Newtonsoft.Json.Linq;
using JsonUIBeautifier.Utils;
using JsonUIBeautifier.Stylizer.Models;

namespace JsonUIBeautifier.Stylizer.Resolvers;

public static class ControlRoleResolver
{
    public static ControlRole InferControlRole(string type, JToken token, JObject? root = null)
    {
        return type.ToLowerInvariant() switch
        {
            "button" => InferButtonRole(token),
            "label" => InferLabelRole(token),
            "textbox" or "richtextbox" => ControlRole.Input,
            "combobox" => ControlRole.Input,
            "checkbox" => ControlRole.Input,
            "panel" => ControlRole.Container,
            "groupbox" => ControlRole.Container,
            "datagridview" or "listview" or "datagrid" => ControlRole.DataTable,
            "listbox" => ControlRole.DataTable,
            "statusbar" or "statusstrip" => ControlRole.StatusBar,
            _ => ControlRole.Default,
        };
    }

    public static TextRole InferTextRole(string type, JToken token)
    {
        var text = token["text"]?.ToString() ?? "";
        var name = token["name"]?.ToString() ?? "";

        if (name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("header", StringComparison.OrdinalIgnoreCase))
            return TextRole.Title;

        if (name.Contains("section", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("group", StringComparison.OrdinalIgnoreCase))
            return TextRole.SectionHeader;

        if (name.Contains("caption", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("hint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tip", StringComparison.OrdinalIgnoreCase))
            return TextRole.Caption;

        if (name.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("muted", StringComparison.OrdinalIgnoreCase))
            return TextRole.Muted;

        var font = token["font"];
        if (font is JObject fontObj)
        {
            var fontName = fontObj["name"]?.ToString() ?? "";
            if (fontName.Contains("consolas", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("cascadia", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("mono", StringComparison.OrdinalIgnoreCase))
                return TextRole.Code;
        }

        return type.ToLowerInvariant() switch
        {
            "label" when text.Length > 20 => TextRole.BodyText,
            "label" => TextRole.Caption,
            "textbox" or "richtextbox" when IsCodeInput(token) => TextRole.Code,
            "textbox" or "richtextbox" => TextRole.BodyText,
            _ => TextRole.BodyText,
        };
    }

    public static bool IsCodeInput(JToken token)
    {
        var name = token["name"]?.ToString() ?? "";
        var font = token["font"];
        if (font is JObject fontObj)
        {
            var fontName = fontObj["name"]?.ToString() ?? "";
            if (fontName.Contains("consolas", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("mono", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return name.Contains("code", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("script", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("log", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("output", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("detail", StringComparison.OrdinalIgnoreCase);
    }

    private static ControlRole InferButtonRole(JToken token)
    {
        var text = token["text"]?.ToString() ?? "";
        var name = token["name"]?.ToString() ?? "";

        var primaryKeywords = new[] { "提交", "保存", "确认", "确定", "执行", "启动",
                                       "submit", "save", "confirm", "ok", "execute", "start",
                                       "create", "add", "new", "send", "upload" };
        var secondaryKeywords = new[] { "取消", "删除", "清空", "重置", "返回",
                                         "cancel", "delete", "clear", "reset", "back",
                                         "remove", "close" };

        if (primaryKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                      name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return ControlRole.PrimaryButton;

        if (secondaryKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return ControlRole.SecondaryButton;

        return ControlRole.SecondaryButton;
    }

    private static ControlRole InferLabelRole(JToken token)
    {
        var text = token["text"]?.ToString() ?? "";
        var name = token["name"]?.ToString() ?? "";

        if (name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("header", StringComparison.OrdinalIgnoreCase))
            return ControlRole.Title;

        if (name.Contains("section", StringComparison.OrdinalIgnoreCase))
            return ControlRole.SectionHeader;

        if (name.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("hint", StringComparison.OrdinalIgnoreCase))
            return ControlRole.Caption;

        return ControlRole.BodyText;
    }
}
