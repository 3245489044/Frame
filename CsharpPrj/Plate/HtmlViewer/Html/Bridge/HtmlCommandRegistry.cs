#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 命令分发表（通用，零领域）——把 <c>cmd → handler</c> 注册为保序表，消除领域库里的
/// <c>if (cmd == …)</c> 长链；<see cref="Commands"/> 即注册序（装配对账 / 白名单单一来源）。
/// handler 形如 <c>(context, payload) → RenderOutput</c>；未命中由 <see cref="HtmlMessageBridge{TContext}"/> 走默认命令。
/// </summary>
/// <typeparam name="TContext">handler 上下文类型（由消费方注入，零领域约束）。</typeparam>
public sealed class HtmlCommandRegistry<TContext>
{
    private readonly Dictionary<string, Func<TContext, HtmlBridgePayload, Task<RenderOutput>>> _handlers;
    private readonly List<string> _order = new();
    private readonly string _separator;

    /// <param name="comparer">查表比较器（默认忽略大小写）。</param>
    /// <param name="allowedSeparator">允许命令串连接符（默认 <c>" | "</c>）。</param>
    public HtmlCommandRegistry(IEqualityComparer<string>? comparer = null, string allowedSeparator = " | ")
    {
        _handlers = new Dictionary<string, Func<TContext, HtmlBridgePayload, Task<RenderOutput>>>(
            comparer ?? StringComparer.OrdinalIgnoreCase);
        _separator = allowedSeparator;
    }

    /// <summary>注册命令 handler（注册序即 <see cref="Commands"/> 序；同键重复注册 = 覆盖 handler 并再登记一个序位）。</summary>
    public void Register(string cmd, Func<TContext, HtmlBridgePayload, Task<RenderOutput>> handler)
    {
        _handlers[cmd] = handler;
        _order.Add(cmd);
    }

    /// <summary>解析命令（忽略大小写）；命中 → handler。</summary>
    public bool TryResolve(string cmd, out Func<TContext, HtmlBridgePayload, Task<RenderOutput>> handler)
        => _handlers.TryGetValue(cmd, out handler!);

    /// <summary>已注册命令清单（注册序；装配对账 / 诊断用）。</summary>
    public IReadOnlyList<string> Commands => _order;

    /// <summary>允许命令串（诊断文案单一来源；由注册表派生）。</summary>
    public string Allowed => string.Join(_separator, _order);
}
