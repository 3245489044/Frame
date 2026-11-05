namespace ContractFramework.Abstractions;

/// <summary>
/// 契约违例（结构事实描述：契约 + 目标 + 细节）。证据须为结构事实，禁执行者叙述。
/// </summary>
public sealed record ContractViolation(ContractId ContractId, string Target, string Detail)
{
    public override string ToString() => $"{ContractId}: [{Target}] {Detail}";
}
