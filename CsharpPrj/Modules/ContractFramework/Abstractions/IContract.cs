namespace ContractFramework.Abstractions;

/// <summary>
/// 契约执行体端口（纯托管、无外部依赖时的直连形态）。
/// <para>与 <see cref="IContractHost"/> 的关系：<c>IContract</c> 为单契约实现体，<c>IContractHost</c> 为其统一适配外观（按种类分派）。</para>
/// </summary>
public interface IContract
{
    /// <summary>执行契约（返回三值裁决 + 违例清单）。</summary>
    ContractResult Validate(IContractContext context);
}
