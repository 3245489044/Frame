using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.ContextBinding;

namespace Share.Conversation.View;

/// <summary>端口读出的单帧（域无关；不含呈现判断）。</summary>
/// <param name="Kind">帧类别（<see cref="FrameTable"/> 主键）。</param>
/// <param name="Text">正文。</param>
/// <param name="Round">轮次（0 = 会话级）。</param>
/// <param name="Seq">域内到达序（稳定排序用）。</param>
/// <param name="Presentation">写侧呈现词（可空 ⇒ 查 <see cref="FrameTable"/>）。</param>
/// <param name="Terminal">写侧终答事实（可空 = 未知，不冒充 false）。</param>
/// <param name="Archived">写侧归档事实（可空 = 未知）。</param>
/// <param name="Fields">原始帧字段袋（可选，供诊断/对账）。</param>
public sealed record ViewFrame(
    string Kind,
    string Text,
    int Round,
    long Seq,
    string? Presentation = null,
    bool? Terminal = null,
    bool? Archived = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// 读源端口（依赖反转的落点）：声明只引用 <see cref="Domain"/> + 选择器，装配器不认识具体数据模型。
/// <para>新增数据源 = 注册一个实现 + 声明一行；禁改装配器。</para>
/// </summary>
public interface IFrameSource
{
    /// <summary>端口域键（= 声明 <c>read-paths.paths[].domain</c>）。</summary>
    string Domain { get; }

    /// <summary>按域内选择器读帧。</summary>
    /// <param name="container">容器键（会话/任务键）。</param>
    /// <param name="selector">域内选择器（受控词；空 = 域缺省）。</param>
    /// <param name="ct">取消令牌。</param>
    ValueTask<IReadOnlyList<ViewFrame>> ReadAsync(string container, string selector, CancellationToken ct);
}

/// <summary>读面请求（UI 唯一入参形态：视图键 + 容器键）。</summary>
/// <param name="View">视图键（= <see cref="ReadPlan.View"/>）。</param>
/// <param name="Container">容器键。</param>
public sealed record ViewQuery(string View, string Container);

/// <summary>
/// 运行时唯一装配器：查表 + 调端口 + 拼快照，**零业务判断**。
/// <para>不做「谁是答案」（读事实派生状态）、不做「哪个面」（查 <see cref="FrameTable"/>）。</para>
/// </summary>
public sealed class ViewEngine
{
    private readonly Dictionary<string, IFrameSource> _sources;

    /// <summary>按端口集合装配（域键唯一；重复 ⇒ 抛）。</summary>
    /// <param name="sources">已注册的读源端口。</param>
    public ViewEngine(IEnumerable<IFrameSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = new Dictionary<string, IFrameSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Domain))
            {
                throw new ArgumentException("IFrameSource.Domain 不得为空", nameof(sources));
            }

            if (!_sources.TryAdd(source.Domain, source))
            {
                throw new InvalidOperationException($"读源域重复：{source.Domain}（端口唯一注册）");
            }
        }
    }

    /// <summary>装配一次视图快照。</summary>
    /// <param name="query">读面请求。</param>
    /// <param name="reads">读计划表（编译产物）。</param>
    /// <param name="frames">帧语义表（编译产物）。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask<ViewSnapshot> BuildAsync(
        ViewQuery query, ReadPlanTable reads, FrameTable frames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (reads is null || frames is null) return ViewSnapshot.Unknown(query.View, "compiled-tables-missing");
        if (!reads.TryGet(query.View, out var plan) || plan is null)
        {
            return ViewSnapshot.Unknown(query.View, "read-plan-missing");
        }

        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["planFingerprint"] = plan.Fingerprint,
            ["framesFingerprint"] = frames.Fingerprint,
        };
        var unavailable = 0;
        var spineChosen = false;
        var spineFrames = new List<(ReadSource Source, ViewFrame Frame)>();
        var supplementFrames = new List<(ReadSource Source, ViewFrame Frame)>();

        foreach (var source in plan.Sources.OrderBy(static s => s.Precedence))
        {
            var isSpine = source.Role == ReadSourceRoles.Spine;

            // spine：首非空即用——一旦采用，后续 spine 源不再读取（不参与合并）。
            if (isSpine && spineChosen)
            {
                meta[$"source.{source.Domain}"] = "skipped:spine-satisfied";
                continue;
            }

            if (!_sources.TryGetValue(source.Domain, out var port))
            {
                unavailable++;
                meta[$"source.{source.Domain}"] = "unavailable";
                continue;
            }

            IReadOnlyList<ViewFrame> framesRead;
            try
            {
                framesRead = await port.ReadAsync(query.Container, source.Selector, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                unavailable++;
                meta[$"source.{source.Domain}"] = "failed:" + ex.GetType().Name;
                continue;
            }

            meta[$"source.{source.Domain}"] = $"frames={framesRead.Count}";

            if (isSpine)
            {
                if (framesRead.Count == 0) continue;   // 空脊梁：继续尝试下一个 spine
                spineChosen = true;
                foreach (var frame in framesRead) spineFrames.Add((source, frame));
                continue;                              // 补位源仍需读取合并
            }

            // supplement：始终合并（去重/归位），不参与首非空短路。
            foreach (var frame in framesRead) supplementFrames.Add((source, frame));
        }

        // 去重：spine 优先；补位仅补齐 spine 未覆盖的帧（键 = 类别+轮次+域内序）。
        // 同键帧改为**事实并集**（E3：禁先到者整条丢弃后到源的独有事实）。
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var collected = new List<(ReadSource Source, ViewFrame Frame)>(spineFrames.Count + supplementFrames.Count);
        foreach (var entry in spineFrames) AddFrame(collected, seen, entry);
        foreach (var entry in supplementFrames) AddFrame(collected, seen, entry);

        // 终答候选：terminal=true 且满足 Authority + Eligible；Select 顺序即优先级（同级 last-wins）。
        var answer = plan.Answer;
        var candidates = collected
            .Where(entry => IsTerminalCandidate(answer, entry.Frame))
            .OrderBy(entry => SelectRank(answer, entry.Frame.Kind))
            .ThenByDescending(entry => entry.Frame.Round)
            .ThenByDescending(entry => entry.Frame.Seq)
            .ToList();
        var winner = candidates.Count > 0 ? candidates[0] : default;
        var answered = candidates.Count > 0;

        var rows = new List<ViewRow>(collected.Count);
        var hasAnchorAlways = false;
        var hasAnchorIfAnswer = false;

        foreach (var entry in collected)
        {
            var frame = entry.Frame;
            var source = entry.Source;
            frames.TryGet(frame.Kind, out var row);

            // 锚点能力由读源声明（AnchorCapable）与帧锚点策略共同决定；
            // IfAnswer 仅在存在合格终答时才算锚点（否则过程行不得铺成气泡）。
            if (source.AnchorCapable)
            {
                if (row?.Anchor is AnchorPolicy.Always) hasAnchorAlways = true;
                else if (row?.Anchor is AnchorPolicy.IfAnswer) hasAnchorIfAnswer = true;
            }

            var isWinner = winner.Frame is not null
                && winner.Source.Domain == source.Domain
                && FrameKey(winner.Frame) == FrameKey(frame);
            var presentation = isWinner && !string.IsNullOrWhiteSpace(answer.TerminalPresentation)
                ? answer.TerminalPresentation
                : frame.Presentation ?? row?.Presentation ?? string.Empty;

            if (string.IsNullOrWhiteSpace(presentation))
            {
                // 未声明呈现 ⇒ 可见占位（禁静默丢行/静默取默认）。
                meta[$"unknown-kind.{frame.Kind}"] = "presentation-missing";
                presentation = "unknown";
            }

            rows.Add(new ViewRow(
                $"{source.Domain}:{frame.Round}:{frame.Seq}:{frame.Kind}",
                frame.Kind,
                presentation,
                frame.Text,
                frame.Round,
                Array.Empty<ViewRow>(),
                row?.Fold ?? "none"));
        }

        var sawFrame = collected.Count > 0;
        var hasAnchor = hasAnchorAlways || (answered && hasAnchorIfAnswer);
        var ordered = rows.OrderBy(static r => r.Round).ToArray();
        var status = answered
            ? AnswerStatus.Answered
            : !sawFrame && unavailable > 0
                ? AnswerStatus.Unknown
                : !sawFrame
                    ? answer.Absent
                    : hasAnchor
                        ? AnswerStatus.NoAnswer
                        : AnswerStatus.ProcessOnly;

        return new ViewSnapshot(query.View, status, ordered, meta, plan.Fingerprint);
    }

    private static string FrameKey(ViewFrame frame)
    {
        // Seq 是「域内到达序」；缺省 0 不代表同一帧。退回文本哈希，
        // 禁把同轮同 Kind 的多个过程帧并成一帧（原实现会实际丢帧）。
        var seq = frame.Seq > 0
            ? frame.Seq.ToString(CultureInfo.InvariantCulture)
            : "t:" + PromptSnapshotBuilder.Hash(frame.Text);
        return $"{frame.Kind}\u001F{frame.Round}\u001F{seq}";
    }

    /// <summary>同键帧合并：先到者身份胜出，字段袋取并集（后到源独有事实不丢）。</summary>
    private static void AddFrame(
        List<(ReadSource Source, ViewFrame Frame)> collected,
        Dictionary<string, int> seen,
        (ReadSource Source, ViewFrame Frame) entry)
    {
        var key = FrameKey(entry.Frame);
        if (seen.TryGetValue(key, out var index))
        {
            collected[index] = (collected[index].Source, MergeFrame(collected[index].Frame, entry.Frame));
            return;
        }

        seen[key] = collected.Count;
        collected.Add(entry);
    }

    private static ViewFrame MergeFrame(ViewFrame winner, ViewFrame other)
    {
        IReadOnlyDictionary<string, string>? fields = winner.Fields;
        if (other.Fields is { Count: > 0 })
        {
            var merged = winner.Fields is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(winner.Fields, StringComparer.Ordinal);
            foreach (var pair in other.Fields)
            {
                if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value;
            }

            fields = merged;
        }

        return winner with
        {
            Text = string.IsNullOrWhiteSpace(winner.Text) ? other.Text : winner.Text,
            Presentation = winner.Presentation ?? other.Presentation,
            Terminal = winner.Terminal ?? other.Terminal,
            Archived = winner.Archived ?? other.Archived,
            Fields = fields,
        };
    }

    /// <summary>终答资格：terminal=true 且权威级允许、帧类别可投影。</summary>
    private static bool IsTerminalCandidate(AnswerRule rule, ViewFrame frame)
        => frame.Terminal is true
           && IsAuthorityAllowed(rule, frame)
           && (rule.Eligible.Count == 0 || rule.Eligible.Contains(frame.Kind, StringComparer.Ordinal));

    /// <summary>权威级：规则未声明 ⇒ 不限制；声明后帧缺权威事实 ⇒ 不合格（未知不冒充合格）。</summary>
    private static bool IsAuthorityAllowed(AnswerRule rule, ViewFrame frame)
    {
        if (rule.Authority.Count == 0) return true;
        var authority = ReadAuthority(frame);
        return !string.IsNullOrWhiteSpace(authority)
               && rule.Authority.Contains(authority, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadAuthority(ViewFrame frame)
        => ConversationFacts.TextOf(frame.Fields, "authority")
           ?? ConversationFacts.TextOf(frame.Fields, "Authority");

    /// <summary>选择优先级：Select 顺序即优先级；未列出的合格帧排最后（仍可被选中）。</summary>
    private static int SelectRank(AnswerRule rule, string kind)
    {
        for (var index = 0; index < rule.Select.Count; index++)
        {
            if (string.Equals(rule.Select[index], kind, StringComparison.Ordinal)) return index;
        }

        return int.MaxValue;
    }
}
