using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Share.Conversation;

/// <summary>
/// 会话对话日志 —— 纯逻辑模型，零 Agent 依赖。消息列表只增不丢（AX-CORE.InformationMonotonicity）；
/// 会话累计轮次 <see cref="NextRound"/> 单调递增（= 用户消息计数，区别于循环轮次 AgentTaskItem.Round）。
/// <see cref="Stage"/> 是会话工作流步骤标识（声明式，不限定层数/子智能体），仅透传步骤名，不感知具体集合。
/// 序列化结果存 <c>Data.__conversation</c> 归档（随 AgentTaskItem.PayloadJson 全量持久化，重启不丢）。
/// </summary>
public sealed class ConversationLog
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<ConversationMessage> _messages = new();

    /// <summary>全量消息（只增；窗口渲染取最近 window 条，全量仍可查询）。</summary>
    public IReadOnlyList<ConversationMessage> Messages => _messages;

    /// <summary>会话累计轮次（下一用户消息应使用的 Round；单调递增，从 0 起）。</summary>
    public int NextRound { get; private set; }

    /// <summary>当前所在用户轮次（最近一条 user 消息的 Round；无 user 消息为 null）。</summary>
    public int? LastUserRound => _messages.LastOrDefault(m => m.Role == ConversationRole.User)?.Round;

    /// <summary>追加一条用户消息（Round = NextRound+1，推进累计轮次）。</summary>
    public void AppendUser(string text) => Append(ConversationRole.User, null, null, text);

    /// <summary>追加一条 assistant 终答（Round 与最近 user 对齐；无 user 时回退 NextRound）。</summary>
    public void AppendAssistant(string text) => Append(ConversationRole.Assistant, null, null, text);

    /// <summary>追加一条工作流中层子智能体产出（气泡按 Stage/SubAgent 可折叠；Round 与最近 user 对齐）。</summary>
    public void AppendSubAgent(string? stage, string? subAgent, string text)
        => Append(ConversationRole.Tool, stage, subAgent, text);

    /// <summary>追加一条系统消息（Round = 0，会话级）。</summary>
    public void AppendSystem(string text) => Append(ConversationRole.System, null, null, text);

    /// <summary>
    /// 单一追加入口（SingleImplementation）：统一 Round 推导（System=0 / User=NextRound+1 / 其余与最近 user 对齐）与
    /// 角色/阶段装载，四个 <see cref="AppendUser"/> 便捷过载均委托于此，消除逻辑分裂。
    /// </summary>
    public void Append(ConversationRole role, string? stage, string? subAgent, string text)
    {
        var round = role switch
        {
            ConversationRole.System => 0,
            ConversationRole.User => NextRound + 1,
            _ => AlignRound(),
        };
        _messages.Add(new ConversationMessage(role, stage, subAgent, text ?? string.Empty, round, DateTimeOffset.UtcNow));
        if (role == ConversationRole.User) NextRound += 1;
    }

    /// <summary>追加一条已成型消息（保留显式 Round/Payload/AtUtc；供外部对话映射回灌，如 Cline → ur_histories）。User 时推进累计轮次。</summary>
    public void AppendRaw(ConversationMessage message)
    {
        if (message.Role == ConversationRole.User)
            NextRound = Math.Max(NextRound, message.Round);
        _messages.Add(message);
    }

    /// <summary>移除指定轮次（Round=n）的全部消息（会话级 System Round=0 不受影响）；用于幂等重归档同一轮。</summary>
    public void DropRound(int round)
    {
        if (round <= 0) return;
        _messages.RemoveAll(m => m.Round == round);
    }

    /// <summary>获取指定轮次（Round=n，会话级 0 除外）的全部消息；无该轮返回空列表。</summary>
    public IReadOnlyList<ConversationMessage> GetRound(int round)
        => _messages.Where(m => m.Round == round).ToList();

    /// <summary>按轮次分组（升序键）；系统消息（Round=0）归入键 0 组。</summary>
    public IReadOnlyList<KeyValuePair<int, IReadOnlyList<ConversationMessage>>> GroupByRound()
        => _messages
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .Select(g => new KeyValuePair<int, IReadOnlyList<ConversationMessage>>(g.Key, g.ToList()))
            .ToList();

    /// <summary>最近一个活跃轮次（最近一条非系统消息的 Round；空会话返回 null）。</summary>
    public int? CurrentRound => _messages.LastOrDefault(m => m.Role != ConversationRole.System)?.Round;

    /// <summary>上下文是否需要压缩（非系统消息条数 &gt; 阈值；吸收旧 ShouldCompressContext 语义）。</summary>
    public bool NeedsCompaction(int threshold = DefaultCompactionThreshold)
        => CountNonSystem() > threshold;

    /// <summary>
    /// 压缩窗口：前置一条 system 摘要，保留最近 <paramref name="keep"/> 条非系统消息；空历史仅摘要。
    /// 信息不丢失的近似：摘要 + 最近窗口（配合持久化全量归档，见 InformationMonotonicity）。
    /// </summary>
    public IReadOnlyList<ConversationMessage> Compress(int keep = DefaultCompressKeep)
    {
        var result = new List<ConversationMessage>
        {
            new(ConversationRole.System, null, null, "[上下文摘要] 已压缩前文上下文，保留关键信息。", 0, DateTimeOffset.UtcNow),
        };
        result.AddRange(RecentNonSystem(keep));
        return result;
    }

    /// <summary>最近窗口 <paramref name="count"/> 条非系统消息（供 LLM 上下文投影；全量仍在 <see cref="Messages"/> 可查询）。</summary>
    public IReadOnlyList<ConversationMessage> Window(int count) => RecentNonSystem(count).ToList();

    /// <summary>默认压缩触发阈值（对齐旧 ShouldCompressContext 的 10）。</summary>
    public const int DefaultCompactionThreshold = 10;

    /// <summary>默认压缩保留条数（对齐旧 CompressHistory 的 5）。</summary>
    public const int DefaultCompressKeep = 5;

    private int CountNonSystem() => _messages.Count(m => m.Role != ConversationRole.System);

    /// <summary>最近 <paramref name="count"/> 条非 System 消息（窗口语义唯一实现：压缩 / 窗口 / 上下文渲染共用）。</summary>
    private IEnumerable<ConversationMessage> RecentNonSystem(int count)
        => _messages.Where(m => m.Role != ConversationRole.System).TakeLast(Math.Max(1, count));

    /// <summary>渲染最近 <paramref name="window"/> 条（非 System）为 LLM 上下文；includeSystem 前置系统消息。</summary>
    public string ToPromptText(int window, bool includeSystem = true)
    {
        var sb = new StringBuilder();
        if (includeSystem)
            foreach (var m in _messages.Where(m => m.Role == ConversationRole.System))
                sb.AppendLine(m.Text);

        var recent = RecentNonSystem(window).ToList();

        foreach (var m in recent)
            sb.AppendLine($"{RoleLabel(m)}: {m.Text}");

        return sb.ToString().Trim();
    }

    /// <summary>序列化为 JSON（存 Data.__conversation 归档）。</summary>
    public string Serialize() => JsonSerializer.Serialize(_messages, JsonOpts);

    /// <summary>从 JSON 反序列化；缺省/非法 → 空会话；旧数据（无 Stage/SubAgent）向后兼容读取。</summary>
    public static ConversationLog Deserialize(string? json)
    {
        var log = new ConversationLog();
        if (string.IsNullOrWhiteSpace(json)) return log;
        try
        {
            var list = JsonSerializer.Deserialize<List<ConversationMessage>>(json, JsonOpts);
            if (list is not null)
                foreach (var m in list)
                    log._messages.Add(m);
        }
        catch (JsonException)
        {
            // 旧/非法数据 → 空会话（容错，不丢运行；AX-META.ContextPollutionIsolation）
        }
        log.RebuildNextRound();
        return log;
    }

    /// <summary>子智能体产出与 assistant 对齐最近用户轮次；无 user 时回退累计轮次（保底 ≥1）。</summary>
    private int AlignRound() => LastUserRound ?? Math.Max(NextRound, 1);

    /// <summary>反序列化后重建累计轮次（= 最大 user Round）。</summary>
    private void RebuildNextRound()
    {
        NextRound = _messages.Where(m => m.Role == ConversationRole.User).Select(m => m.Round).DefaultIfEmpty(0).Max();
    }

    /// <summary>角色标签（供窗口渲染）。角色名权威 = <see cref="ConversationMessageMappings.RoleName"/>；Stage 非空时附步骤名。</summary>
    private static string RoleLabel(ConversationMessage m)
    {
        var role = ConversationMessageMappings.RoleName(m.Role);
        return m.Role == ConversationRole.Tool && !string.IsNullOrWhiteSpace(m.Stage)
            ? $"{role}({m.Stage})"
            : role;
    }
}
