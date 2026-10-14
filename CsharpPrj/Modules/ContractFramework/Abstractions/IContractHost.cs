namespace ContractFramework.Abstractions;

/// <summary>
/// 外部执行体适配端口：把 ValidationRule / Roslyn 分析器 / 门禁脚本 / 运行期探针统一成契约外观。
/// <para>内置适配器放**消费侧**（不放框架内，保持 BCL-only）。</para>
/// </summary>
public interface IContractHost
{
    /// <summary>执行体种类（受控词，禁散落字面量）。</summary>
    ContractHostKind Kind { get; }

    /// <summary>载体是否齐备（结构事实缺失 ⇒ 返回 <c>false</c> ⇒ 契约判 <c>⊥</c>，不放行）。</summary>
    bool CarrierReady(ContractDescriptor contract, IContractContext context);

    /// <summary>求值（返回三值裁决 + 违例清单）。</summary>
    ContractResult Evaluate(ContractDescriptor contract, IContractContext context);
}
