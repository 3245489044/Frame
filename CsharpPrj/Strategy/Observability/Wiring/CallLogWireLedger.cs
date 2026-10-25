using System.Security.Cryptography;
using System.Text;
using Modules.Wiring.Runtime;
using Strategy.Observability.Ports;

namespace Strategy.Observability.Wiring;

/// <summary>
/// <see cref="IWireLedger"/> 的上层适配：把 wire 投递账记入 <see cref="CallLog"/>（端口调用单一记录点）。
/// <para>方向：Strategy(3) → Modules.Wiring(1)，合法向下依赖；Wiring 侧不感知 CallLog（I2 单写者）。</para>
/// </summary>
public sealed class CallLogWireLedger : IWireLedger
{
    /// <summary>账本端口键（CallLog 内可辨识来源）。</summary>
    public const string PortKey = "WireFabric.Junction.Deliver";

    private readonly CallLog _log;

    public CallLogWireLedger(CallLog log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Record(WireLedgerRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var succeeded = record.Status is WireStatus.Delivered or WireStatus.Adapted;

        _log.Record(new PortCallView(
            PortKey,
            "Deliver",
            new object?[] { record.WireId, record.Form, record.Length, record.Status.ToString() },
            ArgsHash(record),
            record.Generation,
            succeeded,
            succeeded ? null : record.Reason ?? record.Status.ToString(),
            ElapsedMs: 0,
            Result: null,
            AtUtc: DateTimeOffset.UtcNow));
    }

    /// <summary>实参指纹（结构事实；同账同指纹，跨账可比）。</summary>
    private static string ArgsHash(WireLedgerRecord record)
    {
        var text = string.Join('\u0000', record.WireId, record.Form, record.Length.ToString(), record.Status.ToString());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
