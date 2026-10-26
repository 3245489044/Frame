using System;
using System.Collections.Generic;
using System.Globalization;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using WorkFlowMethod.KeySpace.Adapters;
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
    /// <summary>
    /// 取字符串入参（缺键 / null ⇒ 空串）。
    /// <para>
    /// A3 兼容适配器：**不区分「缺键」与「存在且为空」**。新调用点必须使用
    /// <see cref="TryRead"/>（三态）；本方法仅保留给既有调用点，不得新增。
    /// </para>
    /// </summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名（= 声明面 <c>inports=</c> 项）。</param>
    public static string Get(WfContext? context, string name)
        => context is not null
           && context.Parameters.TryGetValue(name, out var value)
           && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// 三态读取（键空间 I1）：缺键 / null ⇒ <see cref="KeyValueState.Unknown"/>，不再以空串冒充「存在且为空」。
    /// 旧 <see cref="Get"/> 保留用于兼容调用点（P1 影子，行为不变）。
    /// </summary>
    public static KeyValue<string> TryRead(WfContext? context, string name)
    {
        var key = KeyRef.Of("workflow.parameter", name);
        if (context is not null
            && context.Parameters.TryGetValue(name, out var value)
            && value is not null)
        {
            return KeyValue<string>.Known(value.ToString() ?? string.Empty);
        }

        return KeyValue<string>.Unknown(
            KeyReasonCodes.DeclaredMissing, $"工作流入参未命中: {name}", key);
    }

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

    /// <summary>
    /// 取 JSON 字符串数组入参（A2）：缺键 / 空白 ⇒ 空集（合法）；非法 / 非数组 ⇒ 空集 + 诊断（不静默）。
    /// </summary>
    /// <param name="context">工作流上下文（可空）。</param>
    /// <param name="name">入参键名（值为 JSON 数组文本）。</param>
    public static IReadOnlyList<string> GetJsonStrings(WfContext? context, string name)
    {
        var read = TryRead(context, name);
        if (read.State != KeyValueState.Known)
        {
            return Array.Empty<string>();
        }

        var parsed = KeySetParse.Parse(read.Value, "workflow.parameter:" + name);
        if (parsed.Diagnostic is not null)
        {
            WorkflowKeyDiagnosticsSink.Instance.Report(parsed.Diagnostic);
        }

        return parsed.Members;
    }
}
