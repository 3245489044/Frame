using System;

namespace Share.Conversation.Ids;

/// <summary>
/// FactId **唯一生成器**（方案 §1.2/§1.3）—— 轮 / 步 / 调用的键只能由本类产生，
/// 其他落点禁自拼分隔串（机检 C1）；键经 <see cref="FactId.ToString"/> 出线格式。
/// <para>
/// 轮号来源单一：会话累计轮 <c>ConversationLog.NextRound</c>（唯一计数源，禁另起一套）。
/// </para>
/// </summary>
public static class FactIds
{
    /// <summary>run 级键（会话 = 任务；线格式为裸会话键）。</summary>
    /// <param name="sessionKey">会话键（= runId = TaskKey = HistoryId）。</param>
    public static FactId Run(string sessionKey)
    {
        RequireId(sessionKey, nameof(sessionKey), "会话键");
        return new FactId(sessionKey, 0);
    }

    /// <summary>轮次键（<c>SessionKey#R&lt;n&gt;</c>，轮号从 1 起）。</summary>
    /// <param name="sessionKey">会话键。</param>
    /// <param name="roundNo">会话累计轮号（≥1）。</param>
    public static FactId Round(string sessionKey, int roundNo)
    {
        RequireId(sessionKey, nameof(sessionKey), "会话键");
        if (roundNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(roundNo), roundNo, "轮号须 ≥1（run 级键请用 FactIds.Run）");
        return new FactId(sessionKey, roundNo);
    }

    /// <summary>步骤键（纯派生：会话键 + 轮号取自入参轮键，不引入新随机源）。</summary>
    /// <param name="roundKey">轮键（须为轮级键，不得为 run 级）。</param>
    /// <param name="stepName">工作流步骤名（与既有 Stage 同一词表）。</param>
    /// <param name="attempt">同一步骤重试代次（≥1，缺省 1）。</param>
    public static FactId Step(FactId roundKey, string stepName, int attempt = 1)
    {
        RequireRoundKey(roundKey, nameof(roundKey));
        RequireId(stepName, nameof(stepName), "步骤名");
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "重试代次须 ≥1");

        return new FactId(roundKey.SessionKey, roundKey.RoundNo, stepName, attempt);
    }

    /// <summary>调用键（纯派生：步骤键 + 回合号 + 外部工具调用标识）。</summary>
    /// <param name="stepKey">步骤键（须为步骤级或调用级键）。</param>
    /// <param name="iteration">执行面 LLM 回合号（≥1）。</param>
    /// <param name="callId">外部工具调用标识（原样保留作末段）。</param>
    public static FactId Call(FactId stepKey, int iteration, string callId)
    {
        if (stepKey.StepName is null)
            throw new ArgumentException("调用键须由步骤键派生（入参缺 StepName）", nameof(stepKey));
        if (iteration <= 0)
            throw new ArgumentOutOfRangeException(nameof(iteration), iteration, "回合号须 ≥1");
        RequireId(callId, nameof(callId), "调用标识");

        return new FactId(stepKey.SessionKey, stepKey.RoundNo, stepKey.StepName, stepKey.Attempt ?? 1,
            iteration, callId);
    }

    /// <summary>从任意键串提取会话键（跨 worker 归并 / 读侧分组用；键非法即 false，不猜测）。</summary>
    /// <param name="key">键串（轮 / 步 / 调用均可）。</param>
    /// <param name="sessionKey">out：会话键。</param>
    public static bool TryGetSessionKey(string? key, out string sessionKey)
    {
        sessionKey = string.Empty;
        if (!FactIdGrammar.TryParse(key, out var id)) return false;
        sessionKey = id.SessionKey;
        return true;
    }

    /// <summary>ID 形态校验（会话键 / 段名共用同一字符集：<see cref="FactIdGrammar.IdPattern"/>）。</summary>
    private static void RequireId(string? value, string parameterName, string label)
    {
        if (!FactIdGrammar.IsId(value))
            throw new ArgumentException($"非法{label} '{value}'（须匹配 {FactIdGrammar.IdPattern}）", parameterName);
    }

    /// <summary>入参须为轮级键（非 run 级）。</summary>
    private static void RequireRoundKey(FactId roundKey, string parameterName)
    {
        if (roundKey.IsRunKey)
            throw new ArgumentException("步骤键须由轮键（RoundNo≥1）派生（入参为 run 级键）", parameterName);
        if (roundKey.StepName is not null)
            throw new ArgumentException("步骤键须由轮键派生（入参已含步骤段）", parameterName);
    }
}
