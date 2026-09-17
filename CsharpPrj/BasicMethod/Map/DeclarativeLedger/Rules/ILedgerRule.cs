namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 契约台账判据：编号 + 对**整本**台账求值并追加违规（跨项 / 跨字段一致性归此处）。
/// <para>
/// 编号由消费方注入（框架不内置任何编号）；违规文案须自带 <c>键路径</c> 以保证可定位。
/// </para>
/// </summary>
public interface ILedgerRule<in TCatalog>
{
    /// <summary>判据编号（拼在违规文案前缀，如 <c>R1</c>）。</summary>
    string Code { get; }

    /// <summary>求值并把违规追加进 <paramref name="violations"/>（可多条；不抛）。</summary>
    void Check(TCatalog catalog, List<string> violations);
}
