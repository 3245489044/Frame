using System;
using System.Collections.Generic;

namespace Share.Signal;

/// <summary>接纳结果（稳定结构，供 fn 输出）。</summary>
public sealed record SignalAdmitResult(
    bool Accepted,
    bool Duplicate,
    SignalId Id,
    SignalKey Key,
    SignalState State,
    int Attempt);

/// <summary>账本记录（编码为 <c>sigledger:id|state|attempt|lease|expiry|updated|payload</c>）。</summary>
public sealed record SignalLedgerRecord(
    SignalId Id,
    SignalKey Key,
    SignalState State,
    int Attempt,
    string? LeaseId,
    long UpdatedAt,
    SignalEnvelope Envelope,
    long LeaseExpiresAt = 0)
{
    public const string Prefix = "sigledger:";

    public SignalLedgerRecord With(SignalState state, int attempt, string? leaseId, long leaseExpiresAt)
        => this with
        {
            State = state,
            Attempt = attempt,
            LeaseId = leaseId,
            LeaseExpiresAt = leaseExpiresAt,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

    public string ToText()
    {
        var lease = string.IsNullOrWhiteSpace(LeaseId) ? "-" : LeaseId!;
        var id = Id.IsEmpty ? SignalId.New().Value : Id.Value;
        return $"{Prefix}{id}|{SignalStateNames.Of(State)}|{Attempt}|{lease}|{LeaseExpiresAt}|{UpdatedAt}|{SignalCodec.Encode(Envelope)}";
    }

    public static SignalLedgerRecord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text!.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        // 最多 7 段：payload 为末段（其内部 '|' 不再参与分段）。
        var parts = text.Substring(Prefix.Length).Split('|', 7);
        if (parts.Length < 5) return null;

        var id = new SignalId(parts[0]);
        // 未知状态词 ⇒ 视为损坏记录（不伪造 Created）。
        var state = SignalStateNames.Parse(parts[1]);
        if (state is null) return null;
        _ = int.TryParse(parts[2], out var attempt);
        var lease = string.Equals(parts[3], "-", StringComparison.Ordinal) ? null : parts[3];

        long leaseExpiresAt = 0L;
        long updatedAt = 0L;
        string payload;
        if (parts.Length >= 7)
        {
            // 7 段：id|state|attempt|lease|expiry|updated|payload
            if (long.TryParse(parts[4], out var expiry)) leaseExpiresAt = expiry;
            if (long.TryParse(parts[5], out var updated)) updatedAt = updated;
            payload = parts[6];
        }
        else if (parts.Length == 6)
        {
            // 旧 6 段格式（无 updated）：UpdatedAt 回退为租约到期时刻，缺失则 0。
            if (long.TryParse(parts[4], out var expiry))
            {
                leaseExpiresAt = expiry;
                updatedAt = expiry;
            }
            payload = parts[5];
        }
        else
        {
            // 更旧 5 段格式（无 lease expiry / updated）。
            payload = parts[4];
        }

        var decoded = SignalCodec.Decode(payload);
        if (!decoded.Ok || decoded.Envelope is null) return null;

        var env = decoded.Envelope;
        return new SignalLedgerRecord(id, SignalKey.Of(env.Scope, env.Target, SignalKinds.Normalize(env.Kind)), state.Value, attempt,
            lease, updatedAt, env, leaseExpiresAt);
    }
}

/// <summary>一次扫掠的状态迁移（供观测器记账）。</summary>
public sealed record SignalSweepChange(SignalKey Key, SignalState State);

/// <summary>扫掠结果（迁移清单 + 删除数）。</summary>
public sealed record SignalSweepResult(IReadOnlyList<SignalSweepChange> Changes, int Deleted);

/// <summary>待触发条目（账本键 + 解码信封）。</summary>
public sealed record PendingSignal(string Key, SignalEnvelope Envelope);
