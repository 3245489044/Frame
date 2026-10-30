#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 页面组件目录（M2/M6）：把 <c>htmlPage.components/formatters/labels</c> 收敛为一次只读快照，
/// 提供「派生模型计算 + 模板选择 + 格式化/文案查表」。
/// <para>零副作用、可缓存；未声明任何组件/格式化/文案 ⇒ <see cref="Empty"/>（渲染器走 C# 兜底）。</para>
/// </summary>
public sealed class UiComponentCatalog : IUiExpressionResources
{
    /// <summary>空目录（无声明时单例；Apply 原样返回基础模型）。</summary>
    public static UiComponentCatalog Empty { get; } = new(null, null, null);

    private readonly Dictionary<string, HtmlComponentDeclaration> _components;
    private readonly Dictionary<string, string> _formatters;
    private readonly Dictionary<string, Dictionary<string, string>> _labels;

    private UiComponentCatalog(
        Dictionary<string, HtmlComponentDeclaration>? components,
        Dictionary<string, string>? formatters,
        Dictionary<string, Dictionary<string, string>>? labels)
    {
        _components = components ?? new Dictionary<string, HtmlComponentDeclaration>(StringComparer.OrdinalIgnoreCase);
        _formatters = formatters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _labels = labels ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>从页面声明构建（null/无声明 → <see cref="Empty"/>）。</summary>
    public static UiComponentCatalog From(UIConfiguration? page)
    {
        if (page is null) return Empty;
        if (page.Components is not { Count: > 0 }
            && page.Formatters is not { Count: > 0 }
            && page.Labels is not { Count: > 0 })
            return Empty;

        var components = page.Components is { Count: > 0 }
            ? new Dictionary<string, HtmlComponentDeclaration>(page.Components, StringComparer.OrdinalIgnoreCase)
            : null;
        var formatters = page.Formatters is { Count: > 0 }
            ? new Dictionary<string, string>(page.Formatters, StringComparer.OrdinalIgnoreCase)
            : null;
        var labels = page.Labels is { Count: > 0 }
            ? page.Labels.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            : null;
        return new UiComponentCatalog(components, formatters, labels);
    }

    /// <summary>是否存在该组件声明。</summary>
    public bool Has(string? componentName)
        => !string.IsNullOrWhiteSpace(componentName) && _components.ContainsKey(componentName!);

    /// <summary>组件声明的模板名（未声明/未指定 → null，调用方用默认模板）。</summary>
    public string? TemplateFor(string? componentName)
        => !string.IsNullOrWhiteSpace(componentName)
           && _components.TryGetValue(componentName!, out var declaration)
            ? declaration.Template
            : null;

    /// <summary>
    /// 派生模型：基础模型副本 + 声明规则逐项求值。求值为 null（缺值/未知算子）⇒ 保留基础值（禁静默清空）。
    /// 无组件声明 ⇒ 原样返回基础模型（零分配）。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Apply(
        string? componentName,
        IReadOnlyDictionary<string, object?> baseModel)
    {
        if (!Has(componentName) || _components[componentName!].Model is not { Count: > 0 } rules)
            return baseModel;

        var model = new Dictionary<string, object?>(baseModel.Count + rules.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in baseModel) model[kv.Key] = kv.Value;

        foreach (var rule in rules)
        {
            var derived = UiExpression.Evaluate(rule.Value, model, this);
            if (derived is null) continue;
            model[rule.Key] = ToModelValue(derived);
        }
        return model;
    }

    /// <summary>文案查表（表名/键大小写不敏感）；未命中返回 null。</summary>
    public string? Label(string table, string key)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(key)) return null;
        return _labels.TryGetValue(table, out var entries)
               && entries.TryGetValue(key, out var text)
            ? text
            : null;
    }

    /// <summary>格式化（先按别名解析，再走内置）；未知格式化器返回 null。</summary>
    public string? Format(string formatter, object? value)
    {
        if (string.IsNullOrWhiteSpace(formatter)) return null;
        var name = _formatters.TryGetValue(formatter, out var mapped) ? mapped : formatter;
        return FormatBuiltin(name, value);
    }

    private static object? ToModelValue(object? value) => value switch
    {
        null => null,
        bool b => b ? "1" : string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value
    };

    private static string? FormatBuiltin(string name, object? value)
    {
        switch (name.ToLowerInvariant())
        {
            case "duration":
                return FormatDuration(value);
            case "number":
                return TryNumber(value, out var n) ? n.ToString("N0", CultureInfo.InvariantCulture) : null;
            case "integer":
                return TryNumber(value, out var i) ? Math.Round(i).ToString(CultureInfo.InvariantCulture) : null;
            case "percent":
                return TryNumber(value, out var p) ? (p * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : null;
            case "size":
                return TryNumber(value, out var bytes) ? FormatSize(bytes) : null;
            case "upper":
                return value?.ToString()?.ToUpperInvariant();
            case "lower":
                return value?.ToString()?.ToLowerInvariant();
            case "date":
                if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return DateTime.TryParse(value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : null;
            default:
                return null;
        }
    }

    private static string? FormatDuration(object? value)
    {
        if (value is TimeSpan span) return FormatDuration(span);
        if (!TryNumber(value, out var ms) || ms < 0) return null;
        return FormatDuration(TimeSpan.FromMilliseconds(ms));
    }

    private static string FormatDuration(TimeSpan span)
        => span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
            : span.TotalMinutes >= 1
                ? $"{span.Minutes}m{span.Seconds:00}s"
                : $"{Math.Max(1, (int)span.TotalSeconds)}s";

    /// <summary>字节量格式化（B / KB / MB；供产物尺寸声明式展示）。</summary>
    private static string FormatSize(double bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#}MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.#}KB"
        : $"{bytes:0}B";

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case int i: number = i; return true;
            case long l: number = l; return true;
            case double d: number = d; return true;
            case float f: number = f; return true;
            case decimal m: number = (double)m; return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                number = parsed; return true;
            default: number = 0; return false;
        }
    }
}

