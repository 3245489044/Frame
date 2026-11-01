using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
/// 运行时唯一装配器：查表 + 调端口 + 拼快照，**零语义判断**。
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
        var rows = new List<ViewRow>();
        var unavailable = 0;
        var sawFrame = false;
        var answered = false;
        var hasAnchor = false;

        foreach (var source in plan.Sources.OrderBy(static s => s.Precedence))
        {
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
            foreach (var frame in framesRead)
            {
                sawFrame = true;
                frames.TryGet(frame.Kind, out var row);
                var isTerminal = frame.Terminal is true;
                var presentation = isTerminal && !string.IsNullOrWhiteSpace(plan.Answer.TerminalPresentation)
                    ? plan.Answer.TerminalPresentation
                    : frame.Presentation ?? row?.Presentation ?? string.Empty;

                if (string.IsNullOrWhiteSpace(presentation))
                {
                    // 未声明呈现 ⇒ 可见占位（禁静默丢行/静默取默认）。
                    meta[$"unknown-kind.{frame.Kind}"] = "presentation-missing";
                    presentation = "unknown";
                }

                answered |= isTerminal;
                hasAnchor |= row?.Anchor is AnchorPolicy.Always or AnchorPolicy.IfAnswer;

                rows.Add(new ViewRow(
                    $"{source.Domain}:{frame.Round}:{frame.Seq}:{frame.Kind}",
                    frame.Kind,
                    presentation,
                    frame.Text,
                    frame.Round,
                    Array.Empty<ViewRow>(),
                    row?.Fold ?? "none"));
            }
        }

        var ordered = rows.OrderBy(static r => r.Round).ToArray();
        var status = answered
            ? AnswerStatus.Answered
            : !sawFrame && unavailable > 0
                ? AnswerStatus.Unknown
                : !sawFrame
                    ? plan.Answer.Absent
                    : hasAnchor
                        ? AnswerStatus.NoAnswer
                        : AnswerStatus.ProcessOnly;

        return new ViewSnapshot(query.View, status, ordered, meta, plan.Fingerprint);
    }
}
