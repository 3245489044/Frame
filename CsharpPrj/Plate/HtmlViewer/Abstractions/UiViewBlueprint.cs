#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Abstractions;

/// <summary>视图根生命周期：on-demand = 进入即挂载、离开即卸载（子树不存在 ⇒ 容器不存在）。</summary>
public enum UiViewLifecycle
{
    OnDemand = 0,
    Persistent,
}

/// <summary>容器角色：region = 区域写目标；stream = append-only 流目标（气泡/日志）。</summary>
public enum UiViewContainerRole
{
    Region = 0,
    Stream,
}

/// <summary>视图容器声明（DOM 名 + 唯一所有者 + 角色）。<paramref name="Key"/> 为 DOM 属性 <c>data-name</c> 值。</summary>
public sealed record UiViewContainer(string Key, string OwnerView, UiViewContainerRole Role)
{
    /// <summary>是否流容器（append-only 面）。</summary>
    public bool IsStream => Role == UiViewContainerRole.Stream;
}

/// <summary>跨视图状态迁移声明（未声明 ⇒ 丢弃并由调用方产可见诊断）。</summary>
public sealed record UiViewStateMigration(string Kind, string Key);

/// <summary>
/// 视图程序（编译产物读取模型，单一事实源）：视图根 + 容器集 + 生命周期 + 状态迁移。
/// <para>本模型不含任何领域词汇（视图枚举/业务区域类型）；容器键即 DOM 名，所有权与生命周期在此一等声明。</para>
/// </summary>
public sealed record UiViewBlueprint(
    string ViewKey,
    UiViewLifecycle Lifecycle,
    IReadOnlyList<UiViewContainer> Containers,
    IReadOnlyList<UiViewStateMigration> StateMigrations)
{
    /// <summary>流容器（无 ⇒ null；一视图至多一个流容器）。</summary>
    public UiViewContainer? StreamContainer => Containers.FirstOrDefault(static c => c.IsStream);

    /// <summary>该视图是否拥有指定容器键。</summary>
    public bool Owns(string containerKey)
        => !string.IsNullOrWhiteSpace(containerKey)
           && Containers.Any(c => string.Equals(c.Key, containerKey, StringComparison.Ordinal));

    /// <summary>容器键集合快照。</summary>
    public IReadOnlyList<string> ContainerKeys => Containers.Select(static c => c.Key).ToArray();
}
