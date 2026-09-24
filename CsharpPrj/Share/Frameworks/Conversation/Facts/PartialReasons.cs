using System.Collections.Generic;

namespace Share.Conversation.Facts;

/// <summary>
/// 不完整原因受控词（<c>partial_reason</c>）—— 取值**受控**（禁自然语言错误串充当判据），
/// 声明面词表 = <c>Config\session-facts.ushar</c> 的 <c>[Obs] RoundTerminate enum=partial_reason</c>（P0-5 同源）。
/// <para>
/// <see cref="IngestMiss"/> 为新增救命档：当 C# 未取到权威终答（A1/A2）且摄取器也没读到执行面事实时标记并**可见化**
/// —— 正是「执行面 1890 字、落库只剩 52 字」那类静默丢失应命中而当前无法命中的状态。
/// </para>
/// </summary>
public static class PartialReasons
{
    /// <summary>无未推进项（完整）。</summary>
    public const string None = "none";

    /// <summary>缺权威终答（有执行面事实但无终答事实）。</summary>
    public const string NoFinalAnswer = "no_final_answer";

    /// <summary>仅有流式叙述（A3 兜底，不得表现为成功未截断）。</summary>
    public const string StreamedOnly = "streamed_only";

    /// <summary>工具被拒（权限 / 审批）。</summary>
    public const string ToolDenied = "tool_denied";

    /// <summary>引擎异常退出。</summary>
    public const string EngineExited = "engine_exited";

    /// <summary>鉴权失败。</summary>
    public const string AuthFailed = "auth_failed";

    /// <summary>超时。</summary>
    public const string Timeout = "timeout";

    /// <summary>摄取未命中（框架与执行面事实皆未取得）—— 静默丢失的可见化档。</summary>
    public const string IngestMiss = "ingest_miss";

    /// <summary>全部受控词（声明序；供校验与机检脚本共用）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        None, NoFinalAnswer, StreamedOnly, ToolDenied, EngineExited, AuthFailed, Timeout, IngestMiss,
    };

    /// <summary>是否为受控词（未知串即拒收 —— 防「自然语言错误串充当判据」）。</summary>
    /// <param name="reason">待判定原因串。</param>
    public static bool IsControlled(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;
        foreach (var candidate in All)
        {
            if (candidate == reason) return true;
        }
        return false;
    }
}
