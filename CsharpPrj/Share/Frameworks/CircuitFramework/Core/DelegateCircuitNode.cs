namespace Share.CircuitFramework.Core;

/// <summary>
/// 委托节点：把 <see cref="ICircuitNode"/> 的实现「类样板」（字段 / 构造器 / NodeType 属性 / 方法壳）收敛为一个委托 ——
/// 一个装配步骤只写「算什么」。NodeType 由 Kind 传入，与 <see cref="CircuitNode.Kind"/> 对应。
/// <para>通用件：零领域类型（Kind 与节点体均由调用方注入）。</para>
/// </summary>
public sealed class DelegateCircuitNode : ICircuitNode
{
    /// <summary>节点体：读具名输入端口 → 返回具名输出端口 → 值（或 null = 无非确定性输出）。</summary>
    public delegate Task<IReadOnlyDictionary<string, object?>> Body(
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken);

    private readonly Body _body;

    public DelegateCircuitNode(string nodeType, Body body)
    {
        NodeType = string.IsNullOrWhiteSpace(nodeType)
            ? throw new ArgumentException("节点 Kind 不可为空。", nameof(nodeType))
            : nodeType;
        _body = body ?? throw new ArgumentNullException(nameof(body));
    }

    /// <inheritdoc />
    public string NodeType { get; }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
        => _body(inputs, cancellationToken);

    /// <summary>
    /// 信号节点：步骤体只产生「该步骤唯一输出端口」的载荷（常为 true 或产物本身），
    /// 框架包装为 <c>{ OutPort: 载荷 }</c> —— 顺序链接型的步骤不再手写端口名与返回字典。
    /// </summary>
    public static DelegateCircuitNode Signal(
        string nodeType,
        string outPort,
        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object?>> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outPort);
        ArgumentNullException.ThrowIfNull(body);

        return new DelegateCircuitNode(nodeType, async (inputs, ct) =>
        {
            var value = await body(inputs, ct).ConfigureAwait(false);
            return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { [outPort] = value };
        });
    }

    /// <summary>
    /// 上下文绑定节点（图配方用）：把「上下文 + 具名输入端口 → 具名输出端口」的步骤体偏应用为节点 ——
    /// 装配步骤体集中为一种签名（<c>(ctx, inputs, ct) → 端口字典</c>），不再逐处手写
    /// <c>(inputs, ct) =&gt; Body(ctx, inputs, ct)</c> 闭包。
    /// </summary>
    public static DelegateCircuitNode For<TContext>(
        string nodeType,
        TContext context,
        Func<TContext, IReadOnlyDictionary<string, object?>, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (context is null) throw new ArgumentNullException(nameof(context));

        return new DelegateCircuitNode(nodeType, (inputs, ct) => body(context, inputs, ct));
    }
}
