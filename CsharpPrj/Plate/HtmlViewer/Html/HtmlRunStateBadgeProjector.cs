#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace UiBackend.Html;

/// <summary>
/// 通用「运行态徽章」行模型投影器 —— 把中性的运行态维度（阶段 / 执行体 / 轮次 / 时长 / 来源）
/// 装填为列表项模板的行模型字段（<c>available/stage/statusClass/status/actor/isActive/round/elapsed/origin</c>），
/// 供 <c>taskItem</c> 之类模板绑定状态徽章。
/// <para>
/// <b>零领域类型依赖</b>：输入是中性 <see cref="RunStateFields"/>（任何层级实体均可映射），
/// 阶段→样式类映射表可按受控词表注入（缺省用 <see cref="DefaultStageClasses"/>）；
/// 领域侧只做「领域模型 → 中性字段」的薄适配（如 <c>LevelRunState → RunStateFields</c>）。
/// </para>
/// 无阶段 / 阶段为空 → 全清空（模板判空 <c>{?available}</c> 隐藏徽章，零回归）。
/// </summary>
public static class HtmlRunStateBadgeProjector
{
    /// <summary>中性运行态字段（与领域模型解耦）。</summary>
    /// <param name="Stage">受控阶段（空 = 无运行态）。</param>
    /// <param name="StatusText">原始状态文本（保真；空则回落 <paramref name="Stage"/>）。</param>
    /// <param name="ActorLabel">执行体显示标签（空 = 无执行体，模板隐藏角标）。</param>
    /// <param name="IsActive">是否进行中（模板 <c>.live</c> 呼吸点）。</param>
    /// <param name="Round">当前轮次（null/≤0 = 无轮次徽章）。</param>
    /// <param name="ElapsedMs">已运行毫秒（null/≤0 = 无时长徽章）。</param>
    /// <param name="Origin">事实来源显示文本（如派生 / 快照 / 实时）。</param>
    public sealed record RunStateFields(
        string? Stage,
        string? StatusText = null,
        string? ActorLabel = null,
        bool IsActive = false,
        int? Round = null,
        long? ElapsedMs = null,
        string? Origin = null);

    /// <summary>默认阶段→样式类映射（受控阶段词 → CSS 类；未知回落 <c>idle</c>）。词表可经参数覆盖。</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultStageClasses =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["started"] = "running",
            ["running"] = "running",
            ["paused"] = "paused",
            ["completed"] = "done",
            ["failed"] = "error",
            ["cancelled"] = "cancelled",
        };

    private const string IdleClass = "idle";

    /// <summary>把中性运行态并入行模型（就地增删状态徽章字段）；无阶段 → 全清空。</summary>
    /// <param name="row">行模型（就地修改）。</param>
    /// <param name="state">中性运行态；null 或阶段为空 → 清空全部徽章字段。</param>
    /// <param name="stageClasses">阶段→样式类映射表；null = 用 <see cref="DefaultStageClasses"/>。</param>
    public static void Apply(
        IDictionary<string, object?> row,
        RunStateFields? state,
        IReadOnlyDictionary<string, string>? stageClasses = null)
    {
        if (state is not null && !string.IsNullOrWhiteSpace(state.Stage))
        {
            row["available"] = "1";
            row["stage"] = state.Stage;
            row["statusClass"] = StageClass(state.Stage, stageClasses);
            row["status"] = string.IsNullOrWhiteSpace(state.StatusText) ? state.Stage : state.StatusText!;
            row["actor"] = state.ActorLabel ?? string.Empty;
            row["isActive"] = state.IsActive ? "1" : string.Empty;
            row["round"] = state.Round is > 0
                ? "R" + state.Round.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            row["elapsed"] = FormatElapsed(state.ElapsedMs);
            row["origin"] = state.Origin ?? string.Empty;
        }
        else
        {
            row["available"] = string.Empty;
            row["stage"] = string.Empty;
            row["statusClass"] = string.Empty;
            row["status"] = string.Empty;
            row["actor"] = string.Empty;
            row["isActive"] = string.Empty;
            row["round"] = string.Empty;
            row["elapsed"] = string.Empty;
            row["origin"] = string.Empty;
        }
    }

    /// <summary>阶段 → 徽章样式类（表驱动，大小写不敏感；未知回落 <c>idle</c>）。</summary>
    public static string StageClass(string? stage, IReadOnlyDictionary<string, string>? stageClasses = null)
    {
        var map = stageClasses ?? DefaultStageClasses;
        return map.TryGetValue(stage?.Trim() ?? string.Empty, out var cls) ? cls : IdleClass;
    }

    /// <summary>已运行时长 → 紧凑文本（如 12s / 3m05s / 1h02m）；空 / 负 → 空串（模板判空隐藏）。</summary>
    public static string FormatElapsed(long? elapsedMs)
    {
        if (elapsedMs is not > 0) return string.Empty;
        var ts = System.TimeSpan.FromMilliseconds(elapsedMs.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h{ts.Minutes:00}m"
            : ts.TotalMinutes >= 1
                ? $"{ts.Minutes}m{ts.Seconds:00}s"
                : $"{System.Math.Max(1, (int)ts.TotalSeconds)}s";
    }
}
