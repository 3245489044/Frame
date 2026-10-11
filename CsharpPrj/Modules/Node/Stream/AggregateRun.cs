using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Modules.Node.Stream;

/// <summary>
/// 一次 run 的**内存聚合帧累积器**（per-run；声明驱动、零领域常量）：同一条位置键在时间上聚合为一条记录，
/// 整体跑完经唯一写门**单行**落库。
/// <para>
/// 差异参数（全部经构造注入，禁在本类硬编码领域常量）：分区键 / 帧类别字段名 / 位置键求值 / 每 kind 上限 /
/// 未成轮拒收的可见化日志键 + 日志出口（<see cref="Action{T1,T2}"/>，宿主注入，框架零日志依赖）。
/// </para>
/// <para>
/// 纪律：① **不是位置载体**（不注册进载体注册表，只作读侧 overlay 源）；
/// ② **按分区键隔离**（禁用进程级单值承载运行中帧，防多任务并发串味）；
/// ③ **只增不丢**（帧一经接受即保序保留；释放以「清空 + 复位轮次」表达）。
/// </para>
/// <para>限流：同 kind **连续同载荷去重** + 每 run 每 kind 上限；被丢弃/拒收条数逐 kind 计数 —— 不做「静默丢」。</para>
/// </summary>
public class AggregateRun
{
    private readonly object _gate = new();
    private readonly List<AggregateRoundBuilder> _rounds = new();
    private readonly Dictionary<string, string> _lastFingerprintOfKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _countOfKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _droppedOfKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _refusedOfKind = new(StringComparer.Ordinal);
    private readonly string _kindFieldName;
    private readonly Func<string, string> _positionKeyOf;
    private readonly string _appendBeforeRoundLogKey;
    private readonly Action<string, object?>? _logSink;
    private long _seq;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _endedAtUtc;

    /// <param name="partitionKey">分区键（一分区恰一条聚合记录）。</param>
    /// <param name="kindFieldName">帧类别字段名（写入帧载荷的键名）。</param>
    /// <param name="positionKeyOf">位置键求值（分区键 → 位置键；用于落库产物与读侧 overlay 同桶）。</param>
    /// <param name="maxPerKindPerRun">每 run 每 kind 的帧上限（防载荷爆炸）。</param>
    /// <param name="appendBeforeRoundLogKey">「未成轮拒收」的可见化日志键（登记在日志词表；每域可各持一枚）。</param>
    /// <param name="logSink">日志出口（宿主注入；null ⇒ 静默，不抛）。</param>
    public AggregateRun(
        string partitionKey,
        string kindFieldName,
        Func<string, string> positionKeyOf,
        int maxPerKindPerRun,
        string appendBeforeRoundLogKey,
        Action<string, object?>? logSink = null)
    {
        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            throw new ArgumentException("聚合容器的分区键不可为空", nameof(partitionKey));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(kindFieldName);
        ArgumentNullException.ThrowIfNull(positionKeyOf);
        ArgumentException.ThrowIfNullOrWhiteSpace(appendBeforeRoundLogKey);
        if (maxPerKindPerRun <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerKindPerRun), maxPerKindPerRun, "上限须 > 0");
        }

        PartitionKey = partitionKey;
        _kindFieldName = kindFieldName;
        _positionKeyOf = positionKeyOf;
        _appendBeforeRoundLogKey = appendBeforeRoundLogKey;
        _logSink = logSink;
        MaxPerKindPerRun = maxPerKindPerRun;
    }

    /// <summary>分区键（帧按它隔离，跨分区不串味）。</summary>
    public string PartitionKey { get; }

    /// <summary>每 run 每 kind 的帧上限。</summary>
    public int MaxPerKindPerRun { get; }

    /// <summary>当前轮次（未 <see cref="BeginRound"/> 前为 <c>-1</c>：此期间的帧被**拒收**）。</summary>
    public int Round { get; private set; } = -1;

    /// <summary>当前存活帧数。</summary>
    public int Count { get { lock (_gate) return _rounds.Sum(r => r.Frames.Count); } }

    /// <summary>本次 run 首次成轮的时刻；未成轮 ⇒ <c>null</c>（不伪造）。</summary>
    public DateTimeOffset? StartedAtUtc { get { lock (_gate) return _startedAtUtc; } }

    /// <summary>最后一帧的到达时刻（未接受任何帧 ⇒ <c>null</c>）。</summary>
    public DateTimeOffset? EndedAtUtc { get { lock (_gate) return _endedAtUtc; } }

    /// <summary>因超限/重复被丢弃的条数（按 kind）。</summary>
    public IReadOnlyDictionary<string, int> DroppedOfKind
    {
        get { lock (_gate) return new Dictionary<string, int>(_droppedOfKind, StringComparer.Ordinal); }
    }

    /// <summary>因**未成轮**被拒收的条数（按 kind；与丢弃分账）。</summary>
    public IReadOnlyDictionary<string, int> RefusedOfKind
    {
        get { lock (_gate) return new Dictionary<string, int>(_refusedOfKind, StringComparer.Ordinal); }
    }

    /// <summary>丢失账（丢弃 + 拒收；与过程面共用同一读数面语义）。</summary>
    public AggregateAccounting Accounting()
    {
        lock (_gate)
        {
            return new AggregateAccounting(
                new Dictionary<string, int>(_droppedOfKind, StringComparer.Ordinal),
                new Dictionary<string, int>(_refusedOfKind, StringComparer.Ordinal),
                _droppedOfKind.Values.Sum(v => (long)v),
                _refusedOfKind.Values.Sum(v => (long)v));
        }
    }

    /// <summary>进入轮次（循环边界）：建轮分组（幂等）+ 置当前轮次；轮不重置到达序。</summary>
    /// <param name="round">轮次号。</param>
    public void BeginRound(int round)
    {
        lock (_gate)
        {
            if (round < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(round), round, "轮次须 ≥ 1（0 轮是伪键语义）");
            }

            if (!_rounds.Any(r => r.Round == round))
            {
                _rounds.Add(new AggregateRoundBuilder(round));
            }

            Round = round;
            _startedAtUtc ??= DateTimeOffset.UtcNow;
            _rounds.Sort((left, right) => left.Round.CompareTo(right.Round));
        }
    }

    /// <summary>
    /// 追加一帧（**唯一发射点**的落点）：构造帧载荷 → 归入当前轮分组。
    /// 被限流丢弃（同 kind 连续同载荷 / 超上限）或被拒收（**未成轮**）时返回 <c>null</c>，
    /// 并分别计入 <see cref="DroppedOfKind"/> / <see cref="RefusedOfKind"/>（可见化，不静默）。
    /// </summary>
    /// <param name="kind">帧类别（禁手写字面量；由域常量给出）。</param>
    /// <param name="fields">载荷可读字段（面板 <c>summary</c> 取值面）。</param>
    /// <param name="atUtc">到达时刻（缺省取当前 UTC；测试可注入固定值）。</param>
    public AggregateFrame? Append(
        string kind,
        IReadOnlyDictionary<string, object?>? fields = null,
        DateTimeOffset? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("帧类别（kind）不可为空", nameof(kind));
        }

        var stamp = atUtc ?? DateTimeOffset.UtcNow;
        var fingerprint = FingerprintOf(kind, fields);

        lock (_gate)
        {
            if (Round < 0)
            {
                _refusedOfKind[kind] = (_refusedOfKind.TryGetValue(kind, out var r) ? r : 0) + 1;
                _logSink?.Invoke(_appendBeforeRoundLogKey, new
                {
                    taskKey = PartitionKey,
                    kind,
                    action = "run 边界未建立（未 BeginRound / 已 Release）：本事件拒收（可见化，不静默）",
                });
                return null;
            }

            var count = _countOfKind.TryGetValue(kind, out var c) ? c : 0;
            if (count >= MaxPerKindPerRun)
            {
                _droppedOfKind[kind] = (_droppedOfKind.TryGetValue(kind, out var d) ? d : 0) + 1;
                return null;
            }

            if (_lastFingerprintOfKind.TryGetValue(kind, out var last) &&
                string.Equals(last, fingerprint, StringComparison.Ordinal))
            {
                _droppedOfKind[kind] = (_droppedOfKind.TryGetValue(kind, out var d2) ? d2 : 0) + 1;
                return null;
            }

            var seq = ++_seq;
            var round = Round;
            var payload = BuildFramePayload(seq, kind, stamp, fields);
            var frame = new AggregateFrame(seq, kind, round, payload, stamp);
            _rounds.First(r => r.Round == round).Frames.Add(frame);
            _countOfKind[kind] = count + 1;
            _lastFingerprintOfKind[kind] = fingerprint;
            _endedAtUtc = stamp;
            return frame;
        }
    }

    /// <summary>当前帧集快照（按轮序 → 帧到达序；帧载荷取内存原文）。</summary>
    public Task<IReadOnlyList<AggregateRound>> SnapshotAsync()
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AggregateRound>>(
                _rounds.Select(r => new AggregateRound(r.Round, r.Frames.ToArray())).ToArray());
        }
    }

    /// <summary>
    /// **单行落库快照**：把整个分区的帧聚合成**一个**载荷（分区 → 轮 → 帧 嵌套；帧数组序 = 到达序）。
    /// 无任何帧 ⇒ 返回 <c>null</c>（不伪造空行）。
    /// </summary>
    public Task<StreamAggregate?> SnapshotAggregateAsync()
    {
        lock (_gate)
        {
            if (_rounds.Sum(r => r.Frames.Count) == 0)
            {
                return Task.FromResult<StreamAggregate?>(null);
            }

            var document = new JsonObject
            {
                ["taskKey"] = PartitionKey,
                ["startedAtUtc"] = _startedAtUtc?.ToString("O"),
                ["endedAtUtc"] = _endedAtUtc?.ToString("O"),
                ["rounds"] = new JsonArray(
                    _rounds.Select(round =>
                    {
                        var frames = new JsonArray();
                        foreach (var frame in round.Frames)
                        {
                            frames.Add(JsonNode.Parse(frame.PayloadJson));
                        }

                        return (JsonNode)new JsonObject
                        {
                            ["round"] = round.Round,
                            ["frames"] = frames,
                        };
                    }).ToArray()),
            };

            return Task.FromResult<StreamAggregate?>(
                new StreamAggregate(PartitionKey, _positionKeyOf(PartitionKey), document.ToJsonString()));
        }
    }

    /// <summary>释放本次 run 的内存帧（落库成功后调用；同 run 只落一次 ⇒ 只释放一次）。</summary>
    public void Release()
    {
        lock (_gate)
        {
            _rounds.Clear();
            _lastFingerprintOfKind.Clear();
            _countOfKind.Clear();
            _droppedOfKind.Clear();
            _refusedOfKind.Clear();
            _seq = 0;
            Round = -1;
            _startedAtUtc = null;
            _endedAtUtc = null;
        }
    }

    /// <summary>帧摘要去重键（同 kind 连续同载荷：字段按名序拼接，避免字段顺序造成假去重）。</summary>
    private static string FingerprintOf(string kind, IReadOnlyDictionary<string, object?>? fields)
        => fields is not { Count: > 0 }
            ? kind
            : kind + "|" + string.Join(
                ";",
                fields.Where(p => p.Value is not null)
                      .OrderBy(p => p.Key, StringComparer.Ordinal)
                      .Select(p => $"{p.Key}={p.Value}"));

    /// <summary>帧载荷（<c>{seq,kind,atUtc,...fields}</c>；轮次由外层 rounds[] 承担，不进帧）。</summary>
    private string BuildFramePayload(
        long seq, string kind, DateTimeOffset atUtc, IReadOnlyDictionary<string, object?>? fields)
    {
        var obj = new JsonObject
        {
            ["seq"] = seq,
            [_kindFieldName] = kind,
            ["atUtc"] = atUtc.ToString("O"),
        };

        if (fields is not null)
        {
            foreach (var pair in fields)
            {
                if (pair.Value is not null)
                {
                    obj[pair.Key] = ToNode(pair.Value);
                }
            }
        }

        return obj.ToJsonString();
    }

    private static JsonNode? ToNode(object value) => value switch
    {
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        _ => JsonValue.Create(value.ToString()),
    };

    /// <summary>轮分组构建器（内部可变；对外只经 <see cref="SnapshotAsync"/> 暴露不可变快照）。</summary>
    private sealed class AggregateRoundBuilder
    {
        public AggregateRoundBuilder(int round) => Round = round;

        public int Round { get; }

        public List<AggregateFrame> Frames { get; } = new();
    }
}
