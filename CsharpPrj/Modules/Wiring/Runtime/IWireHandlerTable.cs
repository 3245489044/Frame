namespace Modules.Wiring.Runtime;

/// <summary>
/// 一次盒调用的上下文：值信封 + scope + 目标盒 + 端口值仓。
/// <para>业务值只经 <see cref="TryGetInput"/> 读取（端口即值槽，I10），禁回读原始载荷字符串。</para>
/// </summary>
public readonly record struct WireDeliveryContext(
    ValueEnvelope Envelope,
    string Scope,
    string BoxId,
    IWirePortStore Ports)
{
    public bool TryGetInput(string portName, out ValueEnvelope value)
        => Ports.TryGet(Scope, BoxId + "." + portName, out value);

    /// <summary>取 <c>Text</c> 输入的字符串值；无值 / 空串 → null（缺失由 Junction 的 required 闸门前置拦截）。</summary>
    public string? TryGetText(string portName)
        => TryGetInput(portName, out var value) && value.Payload is string text && text.Length > 0 ? text : null;
}

/// <summary>目标盒投递处理（Junction 的 post 阶段唯一执行体）。</summary>
public delegate ValueTask WireHandler(WireDeliveryContext context, CancellationToken cancellationToken = default);

/// <summary>
/// 盒实现表：<c>boxId → handler</c>。未登记的盒 = 未绑实现（投递必须显式失败，禁静默 Delivered）。
/// </summary>
public interface IWireHandlerTable
{
    bool TryGet(string boxId, out WireHandler handler);
}

/// <summary>可变实现表（组合根装配期注册，运行期只读）。</summary>
public sealed class WireHandlerTable : IWireHandlerTable
{
    private readonly Dictionary<string, WireHandler> _handlers = new(StringComparer.Ordinal);

    public WireHandlerTable Register(string boxId, WireHandler handler)
    {
        if (string.IsNullOrWhiteSpace(boxId)) throw new ArgumentException("boxId 不可为空", nameof(boxId));
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[boxId] = handler;
        return this;
    }

    public bool TryGet(string boxId, out WireHandler handler)
    {
        if (!string.IsNullOrWhiteSpace(boxId) && _handlers.TryGetValue(boxId, out var found))
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }

    public IReadOnlyCollection<string> BoxIds => _handlers.Keys;

    /// <summary>空表（未绑任何实现；用于 Shadow / 只记账场景）。</summary>
    public static WireHandlerTable Empty { get; } = new();
}
