using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Forms;
using TConfig.Controls;
using Plate.MarkdownViewer;
using Plate.HtmlViewer;


namespace WinFormsValueContract;

/// <summary>
/// 内置值语义契约（类型差异的单点声明，唯一落点）。
/// 吸收 MetaCommand.WinFormsControlService 与 FrontEndDriven.WinFormsControlValueWriter 的类型适配器，
/// 收敛为"类型 → 多语义契约"：ListView 同时声明 Selection（匹配选中+回读 Tag）与 Content（整列表重填），
/// ComboBox/TreeView/DataGridView 共享 Selection 语义——消费方按语义名取用，不再散落控件类型判断。
/// </summary>
internal static partial class BuiltInValueContracts
{
    public static void RegisterAll(IDictionary<string, ControlValueContract> target)
    {
        RegisterMarkdownViewerContract(target);
        RegisterHtmlViewerContract(target);

        RegisterTextContracts(target);
        RegisterSelectionContracts(target);
        RegisterStateContracts(target);
    }

    // ── Text 语义 ──

    private static void RegisterTextContracts(IDictionary<string, ControlValueContract> target)
    {
        RegisterTextContract(target, "textbox", nameof(Control.TextChanged));
        RegisterTextContract(target, "richtextbox", nameof(Control.TextChanged));
        RegisterTextContract(target, "maskedtextbox", nameof(Control.TextChanged));
        RegisterTextContract(target, "label", nameof(Control.TextChanged));
        RegisterTextContract(target, "linklabel", nameof(Control.TextChanged));
        RegisterTextContract(target, "button", nameof(Control.TextChanged));
        RegisterTextContract(target, "domainupdown", nameof(DomainUpDown.TextChanged));
        RegisterDateTimePickerContract(target);
    }

        private static void RegisterMarkdownViewerContract(IDictionary<string, ControlValueContract> target)
        {
            target["markdownviewer"] = Contract(
                "markdownviewer",
                ValueSemantic.Text,
                new Dictionary<ValueSemantic, ValueAccessor>
                {
                    [ValueSemantic.Text] = new(
                        _ => null,
                        (c, v) =>
                        {
                            // 输出 markdown 文本 → WebView2 控件渲染；字号指令（@fontsize:数字）只改字号、不渲染文本。
                            if (c is MarkdownViewer md && v is not null)
                            {
                                var text = v.ToString();
                                if (TryParseFontSizeDirective(text, out var px))
                                    _ = md.ApplyFontSizeAsync(px);
                                else
                                    _ = md.SetMarkdownAsync(text);
                            }
                            return v?.ToString();
                        }),
                    [ValueSemantic.Range] = ValueAccessor.Create<MarkdownViewer>(
                        md => md.FontSizePx,
                        (md, v) =>
                        {
                            // 字号（px）：写入经 ApplyFontSizeAsync 重渲染当前内容（异步 fire-and-forget）。
                            if (v is not null && double.TryParse(v.ToString(), out var px) && px > 0)
                                _ = md.ApplyFontSizeAsync(px);
                            return md.FontSizePx.ToString();
                        })
                });
        }

        private static void RegisterHtmlViewerContract(IDictionary<string, ControlValueContract> target)
        {
            target["htmlviewer"] = Contract(
                "htmlviewer",
                ValueSemantic.Text,
                new Dictionary<ValueSemantic, ValueAccessor>
                {
                    [ValueSemantic.Text] = new(
                        _ => null,
                        (c, v) =>
                        {
                            // 输出 HTML（body 片段）→ WebView2 控件直接渲染（可信通道，不再二次转义）；
                            // 指令（@fontsize:数字 / @theme:light|dark|system）只改字号/主题、不渲染文本。XSS 防线在生产端渲染器（文本 HtmlEncode 后再入 body）。
                            if (c is HtmlViewer hv && v is not null)
                            {
                                var text = v.ToString();
                                if (TryParseFontSizeDirective(text, out var px))
                                    _ = hv.ApplyFontSizeAsync(px);
                                else if (TryParseThemeDirective(text, out var mode))
                                    hv.ThemeMode = mode;
                                else
                                    // 收敛写（Observe 落地）：fire-and-forget 保留原非阻塞语义，但写经收敛确认——渲染完成+桥就绪才 Converged，界内未就绪 Degraded（不再对「写即成功」无观察）。
                                    _ = hv.WriteAsync(text);
                            }
                            return v?.ToString();
                        }),
                    [ValueSemantic.Range] = ValueAccessor.Create<HtmlViewer>(
                        hv => hv.FontSizePx,
                        (hv, v) =>
                        {
                            // 字号（px）：写入经 ApplyFontSizeAsync 重渲染当前内容（异步 fire-and-forget）。
                            if (v is not null && double.TryParse(v.ToString(), out var px) && px > 0)
                                _ = hv.ApplyFontSizeAsync(px);
                            return hv.FontSizePx.ToString();
                        })
                });
        }


    private static void RegisterTextContract(IDictionary<string, ControlValueContract> target, string controlType, string eventName)
    {
        target[controlType] = Contract(
            controlType,
            ValueSemantic.Text,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Text] = new(
                    c => ((Control)c).Text,
                    // 唯一文本写入权威：CRLF 规整 + 按需多行 + 赋值（见 ControlTextNormalizer）——消灭裸 control.Text=value 的换行/多行缺陷。
                    (c, v) => ControlTextNormalizer.WriteText(c, v?.ToString()))
            },
            new Dictionary<ValueSemantic, string> { [ValueSemantic.Text] = eventName });
    }

    private static void RegisterDateTimePickerContract(IDictionary<string, ControlValueContract> target)
    {
        target["datetimepicker"] = Contract(
            "datetimepicker",
            ValueSemantic.Text,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Text] = new(
                    c => ((DateTimePicker)c).Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    (c, v) =>
                    {
                        var dtp = (DateTimePicker)c;
                        if (DateTime.TryParseExact(v?.ToString(), "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                            || DateTime.TryParse(v?.ToString(), CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out dt))
                        {
                            dtp.Value = dt;
                            return dtp.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        }
                        return null;
                    })
            },
            new Dictionary<ValueSemantic, string> { [ValueSemantic.Text] = nameof(DateTimePicker.ValueChanged) });
    }

    private static ControlValueContract Contract(
        string controlType,
        ValueSemantic defaultSemantic,
        IReadOnlyDictionary<ValueSemantic, ValueAccessor> accessors,
        IReadOnlyDictionary<ValueSemantic, string>? valueChangedEvents = null)
        => new(controlType, accessors, valueChangedEvents, defaultSemantic);

    private static IEnumerable<string> SplitLines(object? value)
        => (value?.ToString() ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>解析 markdownviewer 字号指令（形如 "@fontsize:18" / "@FontSize: 18"，大小写不敏感，允许前导空白）。
    /// 命中且数值为正时返回 true 并输出 px；否则 false（视为普通 markdown 文本渲染）。</summary>
    private static bool TryParseFontSizeDirective(string? text, out double px)
    {
        px = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.TrimStart();
        const string prefix = "@fontsize:";
        if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = t.Substring(prefix.Length).Trim();
        return double.TryParse(rest, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out px) && px > 0;
    }

    /// <summary>解析 htmlviewer 主题指令（形如 "@theme:dark" / "@Theme: light"，大小写不敏感，允许前导空白）。
    /// 命中合法模式（light/dark/system）返回 true 并输出 ThemeMode；否则 false（视为普通 HTML 文本渲染）。</summary>
    private static bool TryParseThemeDirective(string? text, out ThemeMode mode)
    {
        mode = ThemeMode.System;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.TrimStart();
        const string prefix = "@theme:";
        if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = t.Substring(prefix.Length).Trim();
        switch (rest.ToLowerInvariant())
        {
            case "light": mode = ThemeMode.Light; return true;
            case "dark": mode = ThemeMode.Dark; return true;
            case "system": mode = ThemeMode.System; return true;
            default: return false;
        }
    }

}
