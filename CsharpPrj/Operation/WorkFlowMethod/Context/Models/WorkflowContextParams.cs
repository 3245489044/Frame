using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace WorkFlowMethod.Orchestration.Context.Models;

/// <summary>
/// 工作流函数入参读取**单一模块**（DUP-7 续）—— <c>fn=</c> 单步体从 <see cref="WfContext.Parameters"/>
/// 取值的唯一入口，消除各 <c>*OpsFunctions</c> 各自手写私有 <c>Param</c> / <c>IntParam</c> 的同形副本。
/// <para>
/// 口径（与声明面 <c>inports=</c> 逐字对齐）：缺键 / null / 空白 ⇒ 空串；调用方按「入参缺失」诊断
/// （不抛、不兜底 —— 对齐 fn 层「不静默」纪律）。
/// </para>
/// </summary>
public static class WorkflowContextParams
{
    /// <summary>取字符串入参（缺键 / null ⇒ 空串）。</summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名（= 声明面 <c>inports=</c> 项）。</param>
    public static string Get(WfContext? context, string name)
        => context is not null
           && context.Parameters.TryGetValue(name, out var value)
           && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>取整型入参（不可解析 ⇒ <paramref name="fallback"/>）。</summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名。</param>
    /// <param name="fallback">缺省值。</param>
    public static int GetInt(WfContext? context, string name, int fallback)
        => int.TryParse(Get(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    /// <summary>取非空字符串入参（空白视为缺省）。</summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名。</param>
    /// <param name="value">取到的非空值（否则空串）。</param>
    /// <returns>true = 已取到非空值。</returns>
    public static bool TryGet(WfContext? context, string name, out string value)
    {
        value = Get(context, name);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>取 JSON 字符串数组入参（缺键 / 空白 / 非法 / 非数组 ⇒ 空集，不抛）。</summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名（值为 JSON 数组文本）。</param>
    public static IReadOnlyList<string> GetJsonStrings(WfContext? context, string name)
    {
        var json = Get(context, name);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray array) return Array.Empty<string>();
            return array
                .Select(item => item?.ToString() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
