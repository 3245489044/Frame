using System;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.Node.Ports;

namespace WorkFlowMethod.KeySpace.Adapters;

/// <summary>
/// 端口键空间适配：<see cref="PortKeySpace{TValue}"/> ⇄ 三态键读取。
/// <para>键形态 = <c>{node}.{port}</c>（定向）或裸 <c>{port}</c>（先到者胜）；词法构造仍由
/// <see cref="PortKeySpace{TValue}.BuildPortKey"/> 单源提供（I9）。</para>
/// </summary>
public sealed class PortKeySpaceAdapter : IKeyReader
{
    private readonly PortKeySpace<object?> _space;

    public PortKeySpaceAdapter(PortKeySpace<object?> space)
        => _space = space ?? throw new ArgumentNullException(nameof(space));

    public KeyValue<object?> Read(KeyRef key)
    {
        if (key.IsEmpty)
        {
            return KeyValue<object?>.NotApplicable(
                KeyReasonCodes.NotDeclared, "空键不参与读取", key);
        }

        return _space.TryGet(key.Value, out var value)
            ? KeyValue<object?>.Known(value)
            : KeyValue<object?>.Unknown(
                KeyReasonCodes.DeclaredMissing, $"端口未命中: {key.Value}", key);
    }

    /// <summary>定向写入 <c>{node}.{port}</c>（返回是否接纳）。</summary>
    public bool WriteDirected(string nodeKey, string port, object? value)
        => _space.TryAdd(nodeKey, port, value!);

    /// <summary>裸端口写入（未被更早节点占用时接纳）。</summary>
    public bool WriteBare(string nodeKey, string port, object? value)
        => _space.TryAddBare(nodeKey, port, value!);

    public IReadOnlyDictionary<string, object?> Snapshot() => _space.Snapshot();
}
