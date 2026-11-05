using System;
using System.Collections.Generic;

namespace PluginHostContract;

/// <summary>
/// 插件宿主运行时门面 —— **宿主跨层读取插件状态的唯一入口**（Process 级单槽）。
/// <para>
/// 写入者唯一：启动层（<c>LauncherBootstrap.TryEnablePluginHost</c>）在启用插件宿主装载面时置入；
/// 消费方（MainForm 等）只读 <see cref="Statuses"/> 渲染，禁自行 new 宿主或改 <see cref="Current"/>。
/// </para>
/// <para>
/// 未启用（null）⇒ 消费方走既有装载面（deps.json + ambient 端口），语义不变、零回归。
/// </para>
/// </summary>
public static class PluginHostRuntime
{
    /// <summary>当前插件宿主（null = 未启用插件宿主装载面）。</summary>
    public static IPluginHost? Current { get; set; }

    /// <summary>当前扩展代数（宿主可见化；未启用 = 0）。</summary>
    public static int Generation { get; set; }

    /// <summary>逐插件状态快照（未启用 ⇒ 空；调用方显式走旧路径）。</summary>
    public static IReadOnlyList<PluginStatus> Statuses
        => Current?.Statuses ?? Array.Empty<PluginStatus>();
}
