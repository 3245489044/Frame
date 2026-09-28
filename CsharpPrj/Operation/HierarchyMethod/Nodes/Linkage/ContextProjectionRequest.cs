namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 上下文投影请求（框架中立）—— 读投影流入口的入参准备结果。
/// <para>三个键的取值即作用域差异（对齐「node / task / history 的差别本质 = scope + selection + keyPrefix」）：
/// <see cref="Scope"/>（作用域词）、<see cref="Selection"/>（读侧选择面）、<see cref="KeyPrefix"/>（键空间限定）。</para>
/// </summary>
/// <param name="Scope">作用域（node / task / history 等，词表取值由调用方归一）。</param>
/// <param name="Key">作用域键（nodeKey / taskKey / historyId）。</param>
/// <param name="Selection">读侧选择面（<c>Config\nodes\*.json</c> 的 selection 词表 id；空 = 不限）。</param>
/// <param name="KeyPrefix">键空间前缀（空 = 不限）。</param>
/// <param name="WorkflowId">存储命名空间（节点 ThreadId；空 = 默认工作流）。</param>
/// <param name="TaskKey">收集端口的 task 作用域（收集端口恒单 task 作用域；空 = 全量枚举，兼容直跑）。</param>
public sealed record ContextProjectionRequest(
    string Scope,
    string Key,
    string? Selection = null,
    string? KeyPrefix = null,
    string? WorkflowId = null,
    string? TaskKey = null);
