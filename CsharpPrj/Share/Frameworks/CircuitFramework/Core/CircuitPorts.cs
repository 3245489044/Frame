namespace Share.CircuitFramework.Core;

/// <summary>
/// 端口取值 / 组包原语（节点体与配方共用的最小件）：消除各消费方手写
/// <c>inputs.TryGetValue(name, out var v) ? v as T : null</c> 与逐键组包字典的重复。
/// <para>通用件：零领域类型（端口名与载荷均由调用方给出）。</para>
/// <para>语义固定：缺键 / 类型不符 → null（= 「该产物缺席」，由消费方按缺产物门控；不静默抛、不回落）。</para>
/// </summary>
public static class CircuitPorts
{
    /// <summary>具名输入端口取值（缺键 / 类型不符 → null）。</summary>
    public static T? Get<T>(IReadOnlyDictionary<string, object?> inputs, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return inputs.TryGetValue(name, out var value) ? value as T : null;
    }

    /// <summary>输出端口组包（步骤产物 + 顺序链信号端口）。</summary>
    public static IReadOnlyDictionary<string, object?> Pack(params (string Port, object? Value)[] ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        var outputs = new Dictionary<string, object?>(ports.Length, StringComparer.Ordinal);
        foreach (var (port, value) in ports)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(port);
            outputs[port] = value;
        }
        return outputs;
    }

    /// <summary>输出端口组包（步骤体直接返回的 Task 形态）。</summary>
    public static Task<IReadOnlyDictionary<string, object?>> PackAsync(params (string Port, object? Value)[] ports)
        => Task.FromResult(Pack(ports));
}
