namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 工作流命名空间的声明驱动实现（<see cref="IWorkflowNamespace"/> 唯一生产实现）。
/// <para>默认值来自 <c>Config/keyspaces/agent.keyspace.ushar</c> 的 <c>family hierarchy.node</c> / <c>surface hierarchy.node/Endpoint</c>。</para>
/// </summary>
public sealed class DeclaredWorkflowNamespace : IWorkflowNamespace
{
    public DeclaredWorkflowNamespace(string defaultNamespace)
    {
        if (string.IsNullOrWhiteSpace(defaultNamespace))
            throw new ArgumentException("默认工作流命名空间不能为空（须来自 keyspace 声明）", nameof(defaultNamespace));

        Default = defaultNamespace.Trim();
    }

    public string Default { get; }

    public string Resolve(string? explicitValue, string fallbackKey)
        => string.IsNullOrWhiteSpace(explicitValue) ? Default : explicitValue.Trim();
}

/// <summary>
/// 未解析的工作流命名空间（未装配默认值时的显式出口）：缺失即抛，禁静默取默认值。
/// </summary>
public sealed class UnresolvedWorkflowNamespace : IWorkflowNamespace
{
    public static readonly UnresolvedWorkflowNamespace Instance = new();

    public string Default => throw new InvalidOperationException(
        "工作流命名空间默认值未装配：请在 keyspace 声明默认作用域，或由调用方显式传 namespace"
        + "（禁静默回退 agent-round / 空串）。");

    public string Resolve(string? explicitValue, string fallbackKey)
        => string.IsNullOrWhiteSpace(explicitValue)
            ? throw new InvalidOperationException(
                $"工作流命名空间缺失（fallbackKey='{fallbackKey}'）：请在 keyspace 声明默认作用域，"
                + "或由调用方显式传 namespace（禁静默回退）。")
            : explicitValue.Trim();
}
