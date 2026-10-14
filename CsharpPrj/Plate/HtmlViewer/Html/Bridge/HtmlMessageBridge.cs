#nullable enable

using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// htmlviewer 消息桥（通用框架，零领域）——领域库的**唯一桥入口**：
/// 解析负载（<see cref="HtmlBridgePayloadReader"/>）→ 按框架命令键 <see cref="HtmlBridgePayload.Command"/>
/// 分发（<see cref="HtmlCommandRegistry{TContext}"/>）→ 未命中回退默认命令（保留「未知 cmd ⇒ 默认视图」的 fall-through 语义）。
/// <para>领域侧只需构造一次注册表并调用 <see cref="DispatchAsync(string?, TContext)"/>，
/// 不再手写负载解析与 <c>if (cmd==…)</c> 分发链；领域字段由 handler 从 <see cref="HtmlBridgePayload"/> 自取。</para>
/// </summary>
/// <typeparam name="TContext">handler 上下文类型。</typeparam>
public sealed class HtmlMessageBridge<TContext>
{
    private readonly HtmlCommandRegistry<TContext> _registry;
    private readonly string? _defaultCommand;

    /// <param name="registry">命令分发表。</param>
    /// <param name="defaultCommand">默认命令（cmd 缺失 / 未命中时的回退）；null = 无默认（未命中 ⇒ <see cref="RenderOutput.Skip"/>）。</param>
    public HtmlMessageBridge(HtmlCommandRegistry<TContext> registry, string? defaultCommand = null)
    {
        _registry = registry;
        _defaultCommand = defaultCommand;
    }

    /// <summary>默认命令（cmd 缺失 / 未命中时的回退）。</summary>
    public string? DefaultCommand => _defaultCommand;

    /// <summary>解析负载（异常 / 缺失 ⇒ <see cref="HtmlBridgePayload.Empty"/>，不抛）。</summary>
    public HtmlBridgePayload Parse(string? payload) => HtmlBridgePayloadReader.Parse(payload);

    /// <summary>解析 + 分发；未命中且默认命令未注册 ⇒ <see cref="RenderOutput.Skip"/>（不写）。</summary>
    public Task<RenderOutput> DispatchAsync(string? payload, TContext context)
        => DispatchAsync(HtmlBridgePayloadReader.Parse(payload), context);

    /// <summary>按已解析负载分发；未命中且默认命令未注册 ⇒ <see cref="RenderOutput.Skip"/>。</summary>
    public Task<RenderOutput> DispatchAsync(HtmlBridgePayload payload, TContext context)
    {
        var command = payload.Command;
        if (command is not null && _registry.TryResolve(command, out var handler))
            return handler(context, payload);
        if (_defaultCommand is not null && _registry.TryResolve(_defaultCommand, out var fallback))
            return fallback(context, payload);
        return Task.FromResult(RenderOutput.Skip);
    }
}
