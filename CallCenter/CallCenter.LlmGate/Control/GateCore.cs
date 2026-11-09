using CallCenter.Cost.Costing;
using CallCenter.Cost.Pricing;
using CallCenter.LlmGate.Costing;
using CallCenter.LlmGate.Ledger;
using CallCenter.LlmGate.Model;
using CallCenter.LlmGate.Ports;
using CallCenter.LlmGate.Rules;

namespace CallCenter.LlmGate.Control;

/// <summary>
/// 门内核：规则裁决 + 计数器 + 待审批 + 账本 + 读模型（同时实现控制/查询端口）。
/// 确定性写入单点（计数器/账本/待审批均由本类写）。
/// </summary>
public sealed class GateCore : ILlmGateControl, ILlmGateQuery
{
    private readonly GateOptions _options;
    private readonly GateRuleTable _rules;
    private readonly WindowCounters _windows = new();
    private readonly CallRingBuffer _buffer;
    private readonly PendingRegistry _pending;
    private readonly JsonlCallSink _sink;
    private readonly GateCosting _costing;
    private readonly TimeProvider _time;
    private readonly string _traceId;
    private readonly DateTimeOffset _startedAt;

    private string _mode;
    private bool _halted;
    private string? _haltReason;
    private long _seq;
    private long _coldWriteFailures;

    public GateCore(
        GateOptions options,
        IPriceTable? prices = null,
        string? dataRoot = null,
        TimeProvider? time = null,
        JsonlCallSink? sink = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rules = new GateRuleTable(_options);
        _buffer = new CallRingBuffer(_options.RingSize);
        _pending = new PendingRegistry(_options.MaxPending);
        _sink = sink ?? new JsonlCallSink(dataRoot ?? _options.DataRoot);
        _costing = new GateCosting(prices);
        _time = time ?? TimeProvider.System;
        _startedAt = _time.GetUtcNow();
        _traceId = Guid.NewGuid().ToString("N")[..8];
        _mode = GateModes.IsValid(_options.Mode) ? _options.Mode : GateModes.Enforce;
    }

    public GateOptions Options => _options;
    public string Mode => Volatile.Read(ref _mode);
    public bool Halted => Volatile.Read(ref _halted);
    public string? HaltReason => _haltReason;
    public PendingRegistry Pending => _pending;
    public CallRingBuffer Buffer => _buffer;
    public GateCosting Costing => _costing;
    public long ColdWriteFailures => Interlocked.Read(ref _coldWriteFailures);
    public string Upstream => _options.Upstream;
    public string Listen => _options.Listen;

    /// <summary>分配调用 ID（traceId:seq，一次物理调用唯一）。</summary>
    public string NextCallId() => $"{_traceId}:{Interlocked.Increment(ref _seq)}";

    /// <summary>调用前裁决（计数在裁决后写入：拒绝也计入，循环/速率持续可识别）。</summary>
    public GateRuleResult Evaluate(CallCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var now = _time.GetUtcNow();

        if (Halted)
        {
            _windows.Add(capture.RequestFingerprint, capture.SessionKey, now);
            return GateRuleResult.Deny(RuleIds.Halt, ReasonCodes.Halted,
                $"LlmGate 已 Halt：{_haltReason ?? "未提供原因"}");
        }

        if (string.Equals(Mode, GateModes.Off, StringComparison.Ordinal))
        {
            _windows.Add(capture.RequestFingerprint, capture.SessionKey, now);
            return GateRuleResult.Allow(RuleIds.Bypass, "off 模式：只记录不拦截");
        }

        if (string.Equals(Mode, GateModes.Manual, StringComparison.Ordinal))
        {
            _windows.Add(capture.RequestFingerprint, capture.SessionKey, now);
            return GateRuleResult.Hold(RuleIds.Manual, ReasonCodes.HoldManual, "manual 模式：全部请求转人工");
        }

        var (fingerprintCount, sessionCalls) = _windows.Snapshot(
            capture.RequestFingerprint, capture.SessionKey, now, _options.LoopWindowSec);
        _windows.Add(capture.RequestFingerprint, capture.SessionKey, now);

        var (sessionCost, taskCost, dailyCost, dailyTokens, unknownCosts) = CollectCosts(capture);
        var ctx = new GateContext(
            capture.CallId,
            capture.RequestFingerprint,
            capture.Hop,
            capture.SessionKey,
            capture.TaskKeyResolved,
            capture.ClientId,
            fingerprintCount,
            sessionCalls,
            sessionCost,
            taskCost,
            dailyCost,
            dailyTokens,
            capture.EstimatedCostUsd,
            capture.PriceKnown,
            Halted: false,
            unknownCosts);

        return _rules.Evaluate(ctx);
    }
    /// <summary>挂起等待人工裁决（超时默认拒绝）。</summary>
    public async Task<PendingResolution> HoldAsync(CallCapture capture, GateRuleResult rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(rule);

        var entry = new PendingEntry
        {
            CallId = capture.CallId,
            StartedAt = _time.GetUtcNow(),
            ClientId = capture.ClientId,
            SessionId = capture.SessionId,
            TaskKey = capture.TaskKey,
            Model = capture.Model,
            EstimatedCostUsd = capture.EstimatedCostUsd,
            RuleId = rule.RuleId,
            ReasonCode = rule.ReasonCode ?? ReasonCodes.HoldCost,
            Fingerprint = capture.RequestFingerprint,
        };

        if (!_pending.TryAdd(entry))
        {
            return new PendingResolution(
                GateDecision.Deny, DecidedBy.Rule, ReasonCodes.DenyPendingOverflow,
                $"待审批队列已满（{_pending.MaxPending}）");
        }

        var delay = Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, _options.HoldTimeoutMs)), _time);
        var winner = await Task.WhenAny(entry.Completion.Task, delay).ConfigureAwait(false);
        if (winner != entry.Completion.Task)
        {
            _pending.TryResolve(capture.CallId, GateDecision.Deny, DecidedBy.Timeout,
                ReasonCodes.DenyTimeout, "人工审批超时（默认拒绝）");
        }

        return await entry.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>写入一条调用账本（热缓冲 + 冷 JSONL）。</summary>
    public async ValueTask RecordAsync(LlmCallSpan span, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(span);
        _buffer.Add(span);
        try
        {
            await _sink.AppendCallAsync(span, ct).ConfigureAwait(false);
        }
        catch
        {
            // 冷存失败不阻断 wire；计数可见（Status 输出 coldWriteFailures）。
            Interlocked.Increment(ref _coldWriteFailures);
        }
    }

    /// <summary>构造一条拒绝账本（未转发）。</summary>
    public LlmCallSpan BuildDeniedSpan(CallCapture capture, GateRuleResult rule, DecidedBy decidedBy, DateTimeOffset at)
        => new()
        {
            CallId = capture.CallId,
            TraceId = capture.CallId.Split(':')[0],
            StartedAt = at,
            EndedAt = at,
            DurationMs = 0,
            Endpoint = capture.Endpoint,
            UpstreamBaseUrl = _options.Upstream,
            ModelRequest = capture.Model,
            Stream = capture.Stream,
            Hop = capture.Hop,
            ClientId = capture.ClientId,
            SessionId = capture.SessionId,
            TaskKey = capture.TaskKey,
            RoundId = capture.RoundId,
            MessageCount = capture.MessageCount,
            RoleHistogram = capture.RoleHistogram,
            SystemChars = capture.SystemChars,
            UserChars = capture.UserChars,
            HistoryChars = capture.HistoryChars,
            ToolCount = capture.ToolCount,
            ToolNamesHash = capture.ToolNamesHash,
            RequestBytes = capture.RequestBytes,
            RequestFingerprint = capture.RequestFingerprint,
            StatusCode = rule.HttpStatus,
            ErrorType = "llm_gate_denied",
            ErrorCode = rule.ReasonCode,
            CostAmount = null,
            CostCurrency = "USD",
            CostSource = CostSource.Unknown,
            Decision = GateDecision.Deny,
            ReasonCode = rule.ReasonCode,
            RuleId = rule.RuleId,
            DecidedBy = decidedBy,
            DecidedAt = at,
            RequestBodySampled = _options.StoreBody,
            RequestPreview = _options.StoreBody ? Preview(capture.RequestBody) : null,
        };
    // ================= 控制面 =================

    public string Status()
    {
        var uptime = _time.GetUtcNow() - _startedAt;
        var halted = Halted ? $"是（{_haltReason ?? "未提供原因"}）" : "否";
        return $"mode={Mode} | listen={_options.Listen} | upstream={_options.Upstream} | halted={halted}"
            + $" | uptime={uptime:hh\\:mm\\:ss} | config={_options.ConfigPath ?? "(默认)"}"
            + $" | coldWriteFailures={ColdWriteFailures}";
    }

    public string SetMode(string mode)
    {
        if (!GateModes.IsValid(mode))
        {
            return $"非法模式：{mode}（可用 enforce/manual/off）";
        }

        Volatile.Write(ref _mode, mode);
        return $"模式已切换：{mode}";
    }

    public string Halt(string? reason)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? "未提供原因" : reason!.Trim();
        var already = Halted;
        Volatile.Write(ref _halted, true);
        _haltReason = text;
        if (!already)
        {
            _ = AppendEventSafeAsync(new GateEvent("GateHalted", _time.GetUtcNow(), text, Mode));
        }

        return $"已 Halt：{text}";
    }

    public string Resume()
    {
        var wasHalted = Halted;
        Volatile.Write(ref _halted, false);
        _haltReason = null;
        if (wasHalted)
        {
            _ = AppendEventSafeAsync(new GateEvent("GateResumed", _time.GetUtcNow(), null, Mode));
        }

        return "已 Resume（不清账）";
    }

    public bool TryApprove(string callId, out string message)
    {
        if (_pending.Get(callId) is null)
        {
            message = $"未找到待审批调用：{callId}";
            return false;
        }

        _pending.TryResolve(callId, GateDecision.Allow, DecidedBy.Manual, null, "人工放行");
        message = $"已放行：{callId}";
        return true;
    }

    public bool TryDeny(string callId, string? reason, out string message)
    {
        if (_pending.Get(callId) is null)
        {
            message = $"未找到待审批调用：{callId}";
            return false;
        }

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
        _pending.TryResolve(callId, GateDecision.Deny, DecidedBy.Manual, ReasonCodes.DenyManual, text);
        message = $"已拒绝：{callId}" + (text is null ? string.Empty : $"（{text}）");
        return true;
    }
    // ================= 读面 =================

    public GateStats SnapshotStats()
    {
        var today = _time.GetLocalNow().Date;
        var spans = _buffer.Snapshot()
            .Where(s => s.StartedAt.ToLocalTime().Date == today)
            .ToList();

        var success = spans.Count(s => s.Decision == GateDecision.Allow && s.StatusCode is >= 200 and < 300);
        var failure = spans.Count(s => s.Decision == GateDecision.Allow && s.StatusCode >= 400);
        var denied = spans.Count(s => s.Decision == GateDecision.Deny);

        long tokenTotal = 0;
        var hasTokens = false;
        decimal costTotal = 0;
        var hasCosts = false;
        var unknown = 0;
        foreach (var span in spans)
        {
            if (span.TokenIn.HasValue && span.TokenOut.HasValue)
            {
                tokenTotal += span.TokenIn.Value + span.TokenOut.Value;
                hasTokens = true;
            }

            if (span.CostAmount.HasValue && span.CostSource != CostSource.Unknown)
            {
                costTotal += span.CostAmount.Value;
                hasCosts = true;
            }
            else if (span.Decision == GateDecision.Allow)
            {
                unknown++;
            }
        }

        var top = spans
            .Where(s => !string.IsNullOrEmpty(s.RequestFingerprint))
            .GroupBy(s => s.RequestFingerprint, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return new GateStats(
            Day: today.ToString("yyyy-MM-dd"),
            Calls: spans.Count,
            Success: success,
            Failure: failure,
            Denied: denied,
            Pending: _pending.Count,
            Tokens: hasTokens ? tokenTotal : null,
            Costs: hasCosts ? costTotal : null,
            UnknownCosts: unknown,
            Dropped: _buffer.Dropped,
            TopFingerprint: top is null ? null : top.Key,
            TopFingerprintCount: top?.Count() ?? 0);
    }

    public IReadOnlyList<LlmCallSpan> SnapshotCalls(string? filter, int take)
    {
        IEnumerable<LlmCallSpan> query = _buffer.Snapshot().Reverse();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var needle = filter.Trim();
            query = query.Where(s => Matches(s, needle));
        }

        return query.Take(take <= 0 ? 200 : take).ToList();
    }

    public IReadOnlyList<PendingView> SnapshotPending()
        => _pending.Snapshot().Select(p => p.ToView()).ToList();

    public LlmCallSpan? Find(string callId) => _buffer.Find(callId);

    // ================= 内部 =================

    private (decimal? Session, decimal? Task, decimal? Day, long? Tokens, int Unknown) CollectCosts(CallCapture capture)
    {
        var today = _time.GetLocalNow().Date;
        decimal session = 0, task = 0, day = 0;
        long tokens = 0;
        var hasTokens = false;
        var unknown = 0;

        foreach (var span in _buffer.Snapshot())
        {
            if (span.StartedAt.ToLocalTime().Date != today)
            {
                continue;
            }

            if (span.CostAmount.HasValue && span.CostSource != CostSource.Unknown)
            {
                var amount = span.CostAmount.Value;
                day += amount;

                var spanSession = string.IsNullOrWhiteSpace(span.SessionId) ? span.ClientId : span.SessionId;
                if (string.Equals(spanSession, capture.SessionKey, StringComparison.Ordinal))
                {
                    session += amount;
                }

                var spanTask = string.IsNullOrWhiteSpace(span.TaskKey) ? span.ClientId : span.TaskKey;
                if (string.Equals(spanTask, capture.TaskKeyResolved, StringComparison.Ordinal))
                {
                    task += amount;
                }
            }
            else if (span.Decision == GateDecision.Allow)
            {
                unknown++;
            }

            if (span.TokenIn.HasValue && span.TokenOut.HasValue)
            {
                tokens += span.TokenIn.Value + span.TokenOut.Value;
                hasTokens = true;
            }
        }

        return (session, task, day, hasTokens ? tokens : null, unknown);
    }

    private static bool Matches(LlmCallSpan span, string needle)
    {
        static bool Has(string? value, string term)
            => !string.IsNullOrEmpty(value) && value!.Contains(term, StringComparison.OrdinalIgnoreCase);

        return Has(span.CallId, needle)
            || Has(span.SessionId, needle)
            || Has(span.TaskKey, needle)
            || Has(span.ClientId, needle)
            || Has(span.ModelRequest, needle)
            || Has(span.ModelResponse, needle)
            || Has(span.RequestFingerprint, needle)
            || Has(span.RuleId, needle)
            || Has(span.ReasonCode, needle);
    }

    private string? Preview(string body)
    {
        if (!_options.StoreBody || string.IsNullOrEmpty(body))
        {
            return null;
        }

        var limit = Math.Max(16, _options.PreviewChars);
        return body.Length <= limit ? body : body[..limit];
    }

    private async Task AppendEventSafeAsync(GateEvent evt)
    {
        try
        {
            await _sink.AppendEventAsync(evt).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Increment(ref _coldWriteFailures);
        }
    }
}
