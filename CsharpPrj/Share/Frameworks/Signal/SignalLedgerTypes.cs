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

/// <summary>账本记录（编码为 <c>sigledger:id|state|attempt|lease|expiry|payload</c>）。</summary>
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
        return $"{Prefix}{id}|{SignalStateNames.Of(State)}|{Attempt}|{lease}|{LeaseExpiresAt}|{SignalCodec.Encode(Envelope)}";
    }

    public static SignalLedgerRecord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text!.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var parts = text.Substring(Prefix.Length).Split('|', 6);
        if (parts.Length < 5) return null;

        var id = new SignalId(parts[0]);
        var state = SignalStateNames.Parse(parts[1]) ?? SignalState.Created;
        _ = int.TryParse(parts[2], out var attempt);
        var lease = string.Equals(parts[3], "-", StringComparison.Ordinal) ? null : parts[3];

        var leaseExpiresAt = 0L;
        string payload;
        if (parts.Length == 6 && long.TryParse(parts[4], out var expiry))
        {
            leaseExpiresAt = expiry;
            payload = parts[5];
        }
        else
        {
            payload = parts[4];
        }

        var decoded = SignalCodec.Decode(payload);
        if (!decoded.Ok || decoded.Envelope is null) return null;

        var env = decoded.Envelope;
        return new SignalLedgerRecord(id, SignalKey.Of(env.Scope, env.Target, SignalKinds.Normalize(env.Kind)), state, attempt, lease,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), env, leaseExpiresAt);
    }
}

/// <summary>一次扫掠的状态迁移（供观测器记账）。</summary>
public sealed record SignalSweepChange(SignalKey Key, SignalState State);

/// <summary>扫掠结果（迁移清单 + 删除数）。</summary>
public sealed record SignalSweepResult(IReadOnlyList<SignalSweepChange> Changes, int Deleted);

/// <summary>待触发条目（账本键 + 解码信封）。</summary>
public sealed record PendingSignal(string Key, SignalEnvelope Envelope);
