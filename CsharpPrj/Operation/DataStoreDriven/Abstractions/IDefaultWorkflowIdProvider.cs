namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 默认工作流 Id 提供者（取代核心内硬编码 "agent-round"）。
/// <para>非 Agent 宿主可注入自己的默认值；缺省实现保持既有行为（"agent-round"）。</para>
/// </summary>
public interface IDefaultWorkflowIdProvider
{
    /// <summary>默认工作流 Id（ThreadId 缺失时的回退值）。</summary>
    string DefaultWorkflowId { get; }

    /// <summary>空 ThreadId ⇒ <see cref="DefaultWorkflowId"/>；否则原样返回。</summary>
    string Resolve(string? threadId);
}

/// <summary>默认实现：值来自配置，缺省 "agent-round" 以保持既有行为。</summary>
public sealed class ConfiguredDefaultWorkflowIdProvider : IDefaultWorkflowIdProvider
{
    /// <summary>既有行为的内置缺省值（唯一字面量落点）。</summary>
    public const string BuiltInDefault = "agent-round";

    /// <summary>共享缺省实例（零行为变更）。</summary>
    public static ConfiguredDefaultWorkflowIdProvider Default { get; } = new();

    public ConfiguredDefaultWorkflowIdProvider(string defaultWorkflowId = BuiltInDefault)
        => DefaultWorkflowId = string.IsNullOrWhiteSpace(defaultWorkflowId) ? BuiltInDefault : defaultWorkflowId;

    /// <inheritdoc />
    public string DefaultWorkflowId { get; }

    /// <inheritdoc />
    public string Resolve(string? threadId)
        => string.IsNullOrWhiteSpace(threadId) ? DefaultWorkflowId : threadId!;
}
