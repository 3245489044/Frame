namespace Share.CircuitFramework.Core;

/// <summary>
/// 电路节点黑盒：节点内是 C# 方法实现，图只描述"怎么连、怎么流、怎么控"，不描述"算什么"。
/// 执行层按拓扑推进时逐节点调用本接口。
/// </summary>
public interface ICircuitNode
{
    /// <summary>节点语义类型，与 CircuitNode.Kind 对应。</summary>
    string NodeType { get; }

    /// <summary>执行该黑盒。按具名输入端口取输入，返回具名输出端口 --> 值的映射，或 null（无非确定性输出）。</summary>
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default);
}