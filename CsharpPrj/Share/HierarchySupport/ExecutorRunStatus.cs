namespace Share.HierarchySupport;

/// <summary>一个运行中子执行智能体（如一个 Cline 实例）的实时状态。</summary>
public sealed record ExecutorRunStatus(string ExecutorId, string Status);
