namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 工作流命名空间的声明驱动实现（<see cref="IWorkflowNamespace"/> 唯一生产实现）。
/// <para>默认值来自 <c>Config/keyspaces/agent.keyspace.ushar</c> 的
/// <c>family hierarchy.node</c> / <c>surface hierarchy.node/Endpoint</c>；本类只承载解析，不猜值。</para>
/// </summary>
public sealed class DeclaredWorkflowNamespace : IWorkflowNamespace
{
    public DeclaredWorkflowNamespace(string defaultNamespace)
    {
        if (string.IsNullOrWhiteSpace(defaultNamespace))
            throw new ArgumentException("默认工作流命名空间不能为空（须来自 keyspace 声明）", nameof(defaultNamespace));

        Default = defaultNamespace.Trim();
    }

    /// <inheritdoc />
    public string Default { get; }

    /// <inheritdoc />
    public string Resolve(string? explicitValue, string fallbackKey)
        => string.IsNullOrWhiteSpace(explicitValue) ? Default : explicitValue.Trim();
}
