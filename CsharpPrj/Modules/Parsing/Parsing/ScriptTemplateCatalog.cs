using System;
using System.Collections.Generic;

namespace Modules.Parsing.Parsing;

/// <summary>模板命令的目标取值来源。</summary>
public enum TemplateTargetKind
{
    None,
    Template,
    ControlName,
    ConfigPath
}

/// <summary>模板命令的值取值来源。</summary>
public enum TemplateValueKind
{
    None,
    Title,
    Message,
    ExpectedText,
    Position
}

/// <summary>模板命令的规范化规格（名称 → action / target / value 来源）。</summary>
public readonly record struct ScriptTemplateSpec(
    string Action,
    string? LiteralTarget,
    TemplateTargetKind TargetKind,
    TemplateValueKind ValueKind);

/// <summary>
/// 模板命令目录（D7 单一来源）：04-json-script 的 <c>{template}</c> 展开规则只在此登记。
/// ScriptRunner（FileDriven）与 HtmlScriptBridge（Plate）共用同一张表；未知模板 = TryResolve=false（显式失败，不静默）。
/// </summary>
public static class ScriptTemplateCatalog
{
    private static readonly Dictionary<string, ScriptTemplateSpec> Table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["echo-section"] = new("echo", null, TemplateTargetKind.None, TemplateValueKind.Title),
            ["echo-step"] = new("echo", null, TemplateTargetKind.None, TemplateValueKind.Message),
            ["load-config-show"] = new("load", null, TemplateTargetKind.ConfigPath, TemplateValueKind.None),
            ["validate-positions"] = new("layout", "positions", TemplateTargetKind.Template, TemplateValueKind.None),
            ["validate-report"] = new("layout", "report", TemplateTargetKind.Template, TemplateValueKind.None),
            ["validate-json-report"] = new("layout", "json-report", TemplateTargetKind.Template, TemplateValueKind.None),
            ["validate-overlaps"] = new("layout", "overlaps", TemplateTargetKind.Template, TemplateValueKind.None),
            ["validate-text"] = new("validate", null, TemplateTargetKind.ControlName, TemplateValueKind.ExpectedText),
            ["validate-position"] = new("validate", null, TemplateTargetKind.ControlName, TemplateValueKind.Position)
        };

    public static bool TryResolve(string? template, out ScriptTemplateSpec spec)
    {
        spec = default;
        return !string.IsNullOrWhiteSpace(template) && Table.TryGetValue(template.Trim(), out spec);
    }

    public static IEnumerable<string> Names => Table.Keys;
}
