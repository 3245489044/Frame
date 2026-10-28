namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 未解析的工作流命名空间（未装配默认值时的显式出口）：缺失即抛，禁静默取默认值。
/// <para>触发条件 = 调用方未传 <c>namespace</c> 且宿主未声明默认作用域；属配置缺陷，须 fail-fast 可见化。</para>
/// </summary>
public sealed class UnresolvedWorkflowNamespace : IWorkflowNamespace
{
    /// <summary>单例（无状态）。</summary>
    public static readonly UnresolvedWorkflowNamespace Instance = new();

    /// <inheritdoc />
    public string Default => throw new InvalidOperationException(
        "工作流命名空间默认值未装配：请在 keyspace 声明默认作用域，或由调用方显式传 namespace"
        + "（禁静默回退 agent-round / 空串）。");

    /// <inheritdoc />
    public string Resolve(string? explicitValue, string fallbackKey)
        => string.IsNullOrWhiteSpace(explicitValue)
            ? throw new InvalidOperationException(
                $"工作流命名空间缺失（fallbackKey='{fallbackKey}'）：请在 keyspace 声明默认作用域，"
                + "或由调用方显式传 namespace（禁静默回退）。")
            : explicitValue.Trim();
}
