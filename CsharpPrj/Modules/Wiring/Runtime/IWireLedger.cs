namespace Modules.Wiring.Runtime;

/// <summary>
/// 一条投递账（I2：同一 wire 恰一条账；单写者 = Junction）。
/// <para>字段全部为结构事实（id / 形态 / 长度 / 终态 / 代际），禁承载叙述。</para>
/// </summary>
public sealed record WireLedgerRecord(
    string WireId,
    string OpId,
    string Form,
    int Length,
    WireStatus Status,
    string? Reason = null,
    string? Generation = null);

/// <summary>
/// 账本抽象（上层适配 <c>PortCatalog</c> / <c>CallLog</c>；Modules 不引用上层）。
/// </summary>
public interface IWireLedger
{
    void Record(WireLedgerRecord record);
}

/// <summary>无宿主时的 Null 账本：显式空实现（未装配可见化由调用方承担，不静默吞并结果）。</summary>
public sealed class NullWireLedger : IWireLedger
{
    public static readonly NullWireLedger Instance = new();

    public void Record(WireLedgerRecord record)
    {
        // 显式空实现：无观察者 ≠ 静默丢弃（终态仍由 WireResult 返回）。
    }
}

/// <summary>最小可用账本（测试 / CLI / 无宿主场景）。</summary>
public sealed class InMemoryWireLedger : IWireLedger
{
    private readonly List<WireLedgerRecord> _records = new();
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate) return _records.Count;
        }
    }

    public IReadOnlyList<WireLedgerRecord> Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    public IReadOnlyList<WireLedgerRecord> ForWire(string wireId)
    {
        lock (_gate)
        {
            return _records.Where(record => string.Equals(record.WireId, wireId, StringComparison.Ordinal)).ToArray();
        }
    }

    public void Record(WireLedgerRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate) _records.Add(record);
    }
}
