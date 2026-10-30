#nullable enable

using System;
using System.Collections.Generic;
using Share.HostAssembly.Concurrency;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 宿主槽注册表（薄转发 <see cref="KeyedSlotRegistry{T}"/>）—— 按插件 key 取用各自
/// <see cref="HtmlUiHostSlot"/> 实例（首次取用时创建并缓存），使多插件共享同一套宿主门面机制而**槽位互相隔离**。
/// 未指定 key 的消费方沿用 <see cref="HtmlUiHost.Default"/>（单插件兼容形态）。
/// </summary>
public static class HtmlUiHostRegistry
{
    private static readonly KeyedSlotRegistry<HtmlUiHostSlot> Slots = new(static () => new HtmlUiHostSlot());

    /// <summary>取（或创建）指定插件的宿主槽实例。key 不可为空。</summary>
    public static HtmlUiHostSlot For(string key) => Slots.For(key);

    /// <summary>登记指定插件的宿主槽实例（幂等：覆盖式）。供组合根显式注入自建实例。</summary>
    public static void Register(string key, HtmlUiHostSlot slot) => Slots.Register(key, slot);
}
