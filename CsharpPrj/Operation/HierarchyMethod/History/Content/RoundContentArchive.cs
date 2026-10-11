using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node;
using Modules.Node.Identity;
using Modules.Node.Placement;
using Operation.DataStoreDriven.Abstractions;
using Share.Conversation.Ids;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 子智能体轮次归档实现（**原文全量层**，域无关）—— 一个历史（会话容器）**恰一行**归档节点
/// （单行存储键 = 归档域位置声明的键模板求值；载荷 <c>rounds[]</c> 内嵌套各轮原文）。
/// <para>
/// 载体：归档是**原文全量层**，与入史层职责不同 —— 混写同一容器互相污染（raw round 全文进 LLM 上下文即 prompt 膨胀）。
/// 归档层消费者 = 任务面板「展开详情」/resume 回灌/审计。
/// </para>
/// <para>
/// 键构造单源：本类**不持**键模板常量 —— 单行存储键经归档域位置声明的键模板求值
/// （<see cref="KeyTemplateEvaluator.TryEvaluatePlacement(NodeFramework?, string, IReadOnlyDictionary{string, string}, out string, out string)"/>）。
/// 归档域框架未装配 ⇒ **拒 + 可见化**（不落库、不阻断其余层；禁回退常量）。
/// </para>
/// <para>写路径 = **单行读改写**（读旧行 → 合并本轮 → 恰 1 次写）；未装配节点库 → 各方法降级为空操作/null。</para>
/// </summary>
public sealed class RoundContentArchive : IRoundContentArchive
{
    /// <summary>框架未装配时的归档拒绝事件名（可见化；键构造点不得回退常量）。</summary>
    public const string FrameworkUnboundEvent = "RoundContentArchive_FrameworkUnbound";

    /// <summary>写门拒写事件名（已装配却拒写：只可见化，不回退直写）。</summary>
    public const string MaterializeRejectedEvent = "RoundContentArchive_MaterializeRejected";

    private readonly Func<INodeStore?> _nodeProvider;
    private readonly RoundArchiveSpec _spec;
    private readonly Func<string, string> _sessionKeyMapper;
    private readonly Func<NodeFramework?>? _roundFrameworkProvider;
    private readonly Func<NodeFramework?>? _archiveFrameworkProvider;
    private readonly Action<string, object?>? _logSink;
    private readonly object _writerSync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private NodeContentMaterializeService? _archiveWriter;

    /// <param name="nodeProvider">惰性取节点库（归档层载体）。null → 归档各方法降级为空操作。</param>
    /// <param name="spec">归档域差异参数（位置 id / 类别 / 判别 / 占位符名）。</param>
    /// <param name="sessionKeyMapper">外部会话 id → 归档会话键映射；缺省恒等。</param>
    /// <param name="roundFrameworkProvider">惰性取**轮域**节点框架；仅 legacy 只读回退用。</param>
    /// <param name="archiveFrameworkProvider">惰性取**归档域**节点框架；返回 null → 归档拒 + 可见化。</param>
    /// <param name="logSink">可见化出口（宿主注入；null ⇒ 静默，不抛）。</param>
    public RoundContentArchive(
        Func<INodeStore?> nodeProvider,
        RoundArchiveSpec spec,
        Func<string, string>? sessionKeyMapper = null,
        Func<NodeFramework?>? roundFrameworkProvider = null,
        Func<NodeFramework?>? archiveFrameworkProvider = null,
        Action<string, object?>? logSink = null)
    {
        _nodeProvider = nodeProvider ?? throw new ArgumentNullException(nameof(nodeProvider));
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _sessionKeyMapper = sessionKeyMapper ?? (s => s);
        _roundFrameworkProvider = roundFrameworkProvider;
        _archiveFrameworkProvider = archiveFrameworkProvider;
        _logSink = logSink;
    }

    public async Task ArchiveAsync(string sessionId, int round, string messagesJson, string status, CancellationToken ct = default)
    {
        var nodes = _nodeProvider();
        if (nodes is null) return;
        var key = ResolveSessionKey(sessionId);

        // 归档域唯一写门（单行读改写）：读旧行 → 合并本轮 → 恰 1 次写（键 = 位置声明求值）。
        var framework = _archiveFrameworkProvider?.Invoke();
        var writer = ResolveArchiveWriter();
        var reason = "framework.unbound";
        if (framework is null || writer is null ||
            !TryBuildNodeKey(framework, _spec, key, out var nodeKey, out reason))
        {
            _logSink?.Invoke(FrameworkUnboundEvent, new
            {
                sessionKey = key,
                round,
                status,
                reason,
                action = "backed-off（归档域节点框架未装配：归档不落库，禁回退常量）",
            });
            return;
        }

        // RMW 串行化（同容器内读-改-写不可交错）。
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        NodeContentMaterializeOutcome outcome;
        try
        {
            var existing = await nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
            var merged = RoundArchivePayload.Merge(
                existing is null ? null : Plate.DslMethod.Usharp.UsharpValueJson.ToPayloadText(existing.Payload),
                round, messagesJson, status);
            outcome = await writer
                .MaterializeAsync(key, merged, _specIdValues(key), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        if (!outcome.IsOk)
        {
            _logSink?.Invoke(MaterializeRejectedEvent, new
            {
                sessionKey = key,
                round,
                status,
                outcome = outcome.Describe(),
            });
        }
    }

    /// <summary>惰性解析归档域写门（装配序晚于本类构造 ⇒ 不可构造期解析；解析成功即缓存）。</summary>
    private NodeContentMaterializeService? ResolveArchiveWriter()
    {
        var cached = _archiveWriter;
        if (cached is not null) return cached;

        var framework = _archiveFrameworkProvider?.Invoke();
        if (framework is null) return null;

        lock (_writerSync)
        {
            return _archiveWriter ??= new NodeContentMaterializeService(
                framework, _spec.WriterId, _spec.NodePlacementIds);
        }
    }

    /// <summary>单键域身份值袋（归档域身份模板仅 <c>{container}</c>）。</summary>
    private IReadOnlyDictionary<string, string> _specIdValues(string container)
        => new Dictionary<string, string>(StringComparer.Ordinal) { [_spec.ContainerPlaceholder] = container };

    public async Task<string?> LoadAsync(string sessionId, int round, CancellationToken ct = default)
    {
        var nodes = _nodeProvider();
        if (nodes is null) return null;
        var sessionKey = ResolveSessionKey(sessionId);

        // 新形态优先：单行归档键的 rounds[] 取轮原文。
        var framework = _archiveFrameworkProvider?.Invoke();
        if (framework is not null && TryBuildNodeKey(framework, _spec, sessionKey, out var nodeKey, out _))
        {
            var node = await nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
            if (node is not null)
            {
                var payload = Plate.DslMethod.Usharp.UsharpValueJson.ToPayloadText(node.Payload);
                foreach (var entry in RoundArchivePayload.Parse(payload))
                {
                    if (entry.Round == round) return entry.Messages;
                }
            }
        }

        // 观察期：A2 前的旧键形归档行经 legacy 位置读取（仅新单行未命中该轮时回退）。
        var legacyFramework = _roundFrameworkProvider?.Invoke();
        if (legacyFramework is not null)
        {
            foreach (var placementId in _spec.RoundPlacementIds)
            {
                if (!TryBuildLegacyKeyAt(legacyFramework, placementId, sessionKey, round, out var legacyKey, out _))
                {
                    continue;
                }

                var legacyNode = await nodes.GetAsync(legacyKey, ct).ConfigureAwait(false);
                if (legacyNode is not null) return Plate.DslMethod.Usharp.UsharpValueJson.ToPayloadText(legacyNode.Payload);
            }
        }

        return null;
    }

    public async Task<string?> LoadLatestAsync(string sessionId, CancellationToken ct = default)
    {
        var rounds = await ListRoundsAsync(sessionId, ct).ConfigureAwait(false);
        return rounds.Count == 0 ? null : await LoadAsync(sessionId, rounds[^1], ct).ConfigureAwait(false);
    }

    /// <summary>推导下一归档轮次（已归档最大 + 1；无归档则为 1）。跨 worker 一致、session 自洽递增。</summary>
    public async Task<int> NextRoundAsync(string sessionId, CancellationToken ct = default)
    {
        var rounds = await ListRoundsAsync(sessionId, ct).ConfigureAwait(false);
        return rounds.Count == 0 ? 1 : rounds[^1] + 1;
    }

    public async Task<IReadOnlyList<int>> ListRoundsAsync(string sessionId, CancellationToken ct = default)
    {
        var nodes = _nodeProvider();
        if (nodes is null) return Array.Empty<int>();
        var key = ResolveSessionKey(sessionId);
        var records = await nodes
            .ListAsync(nodeType: _spec.NodeType, threadId: key, limit: int.MaxValue, ct: ct)
            .ConfigureAwait(false);
        if (records is null) return Array.Empty<int>();

        var placements = _roundFrameworkProvider?.Invoke()?.Placements;
        var rounds = new HashSet<int>();
        foreach (var record in records)
        {
            if (_spec.IsArchiveKey(record.NodeKey))
            {
                // 新单行：轮次取载荷 rounds[]（键内无 round 段）。
                foreach (var entry in RoundArchivePayload.Parse(Plate.DslMethod.Usharp.UsharpValueJson.ToPayloadText(record.Payload)))
                {
                    if (entry.Round > 0) rounds.Add(entry.Round);
                }

                continue;
            }

            // legacy 旧多段键：轮次由位置声明反解。
            var round = ParseRound(placements, _spec, record.NodeKey, key);
            if (round > 0) rounds.Add(round);
        }

        return rounds.OrderBy(round => round).ToList();
    }

    /// <summary>
    /// 归档**单行**存储键（唯一构造点，经归档域位置声明的键模板求值）：读侧、写侧与测试按同一键定位，禁各处手拼。
    /// 未装配（框架/位置缺失）或取值不全 ⇒ <c>false</c> + reason（调用方可见化；禁回退常量）。
    /// </summary>
    public static bool TryBuildNodeKey(
        NodeFramework? framework, RoundArchiveSpec spec, string container, out string key, out string reason)
        => KeyTemplateEvaluator.TryEvaluatePlacement(
            framework,
            spec.NodePlacementId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [spec.ContainerPlaceholder] = container,
            },
            out key,
            out reason);

    /// <summary>legacy 旧多段存储键求值（观察期只读回退；值袋给全四占位）。</summary>
    private bool TryBuildLegacyKeyAt(
        NodeFramework? framework, string placementId, string sessionKey, int round, out string key, out string reason)
        => KeyTemplateEvaluator.TryEvaluatePlacement(
            framework,
            placementId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [_spec.ContainerPlaceholder] = sessionKey,
                [_spec.RoundPlaceholder] = round.ToString(CultureInfo.InvariantCulture),
                ["kind"] = _spec.Kind,
                ["ordinal"] = _spec.Ordinal.ToString(CultureInfo.InvariantCulture),
            },
            out key,
            out reason);

    /// <summary>从归档存储键解析轮次（经位置声明反解，禁手写切片）；非本会话/不同形/不可解析 → 0。新形态优先，其次 legacy。</summary>
    public static int ParseRound(PlacementRegistry? placements, RoundArchiveSpec spec, string? nodeKey, string sessionKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey) || string.IsNullOrWhiteSpace(sessionKey)) return 0;

        foreach (var placementId in spec.RoundPlacementIds)
        {
            var round = TryRoundOf(placements, spec, placementId, nodeKey!, sessionKey);
            if (round is not null) return round.Value;
        }

        return 0;
    }

    /// <summary>按指定归档位置反解轮次（不合形 / 非本会话 / 不可解析 ⇒ <c>null</c>，不猜）。</summary>
    private static int? TryRoundOf(
        PlacementRegistry? placements, RoundArchiveSpec spec, string placementId, string nodeKey, string sessionKey)
    {
        if (!KeyTemplateEvaluator.TryParsePlacementValues(
                placements, placementId, nodeKey, out var values, out _))
        {
            return null;
        }

        return values.TryGetValue(spec.ContainerPlaceholder, out var container) &&
               string.Equals(container, sessionKey, StringComparison.Ordinal) &&
               values.TryGetValue(spec.RoundPlaceholder, out var roundText) &&
               int.TryParse(roundText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round)
            ? round
            : null;
    }

    private string ResolveSessionKey(string sessionId)
    {
        // 归一与默认值单源 = 平台层 SessionKeys.Resolve；注入映射器先于归一应用。
        var mapped = string.IsNullOrWhiteSpace(sessionId) ? null : _sessionKeyMapper(sessionId);
        return SessionKeys.Resolve(mapped);
    }
}
