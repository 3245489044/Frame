#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 视图程序目录（不可变值对象）：由 <see cref="IUiDeclarationPort"/> 的实现方装配，本层零解析/零 I/O。
/// <para>单一来源 = 声明面（ui-regions.json）经端口派生；禁回退第二份视图表（缺失 ⇒ 显式失败）。</para>
/// </summary>
public sealed class UiViewBlueprintCatalog
{
    private readonly Dictionary<string, UiViewBlueprint> _byKey;

    /// <summary>由已物化模型构造（唯一构造入口；声明解析在 <see cref="IUiDeclarationPort"/> 实现方）。</summary>
    public UiViewBlueprintCatalog(
        string rootName, string fingerprint, IEnumerable<UiViewBlueprint> views, string sourceName = "ui-declaration")
    {
        ArgumentNullException.ThrowIfNull(views);
        RootName = (rootName ?? string.Empty).Trim();
        Fingerprint = fingerprint ?? string.Empty;
        _byKey = new Dictionary<string, UiViewBlueprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in views)
        {
            if (view is null || string.IsNullOrWhiteSpace(view.ViewKey)) continue;
            _byKey[view.ViewKey.Trim()] = view;
        }

        Validate(sourceName);
    }

    /// <summary>页面视图宿主名（根子集投影的写目标）。</summary>
    public string RootName { get; }

    /// <summary>产物指纹（并入稳定指纹：改视图程序即刷新）。</summary>
    public string Fingerprint { get; }

    /// <summary>视图数。</summary>
    public int Count => _byKey.Count;

    /// <summary>全部视图程序（未排序；选择器顺序由端口实现方给出）。</summary>
    public IEnumerable<UiViewBlueprint> Values => _byKey.Values;

    /// <summary>
    /// 就绪断言（fail-fast）：产物缺失/非法/空视图集 ⇒ 抛。
    /// <para>禁止 fail-open（空目录 ⇒ 不建 ViewHost ⇒ 切视图残留且无显式失败）；
    /// 装配期调用一次，让「产物坏」在启动面可见。</para>
    /// </summary>
    public void EnsureReady(string sourceName = "view-programs")
    {
        if (_byKey.Count == 0)
            throw new InvalidOperationException($"{sourceName}: 视图程序目录为空（产物缺失或非法，禁静默兜底）");
        if (string.IsNullOrWhiteSpace(RootName))
            throw new InvalidOperationException($"{sourceName}: 根宿主名缺失");
    }

    /// <summary>取视图程序（未声明 ⇒ false）。</summary>
    public bool TryGet(string? viewKey, out UiViewBlueprint blueprint)
    {
        if (!string.IsNullOrWhiteSpace(viewKey) && _byKey.TryGetValue(viewKey!.Trim(), out var found))
        {
            blueprint = found;
            return true;
        }

        blueprint = null!;
        return false;
    }

    /// <summary>
    /// 结构校验（唯一实现）：容器键在视图内唯一、所有者与视图一致、至多一个流容器、迁移键 ⊆ 容器键。
    /// <para>判据载体 = 声明结构事实（非执行者叙述）；违反 ⇒ 抛。</para>
    /// </summary>
    public void Validate(string sourceName = "ui-declaration")
    {
        foreach (var blueprint in _byKey.Values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var streams = 0;
            foreach (var container in blueprint.Containers)
            {
                if (!string.Equals(container.OwnerView, blueprint.ViewKey, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"{sourceName}: 容器 '{container.Key}' 所有者 {container.OwnerView} ≠ 视图 {blueprint.ViewKey}");
                if (!seen.Add(container.Key))
                    throw new InvalidOperationException($"{sourceName}: 视图 '{blueprint.ViewKey}' 容器键重复：{container.Key}");
                if (container.IsStream) streams++;
            }

            if (streams > 1)
                throw new InvalidOperationException($"{sourceName}: 视图 '{blueprint.ViewKey}' 流容器多于一个");

            foreach (var migration in blueprint.StateMigrations)
            {
                if (!blueprint.Owns(migration.Key))
                    throw new InvalidOperationException(
                        $"{sourceName}: 视图 '{blueprint.ViewKey}' 状态迁移指向不存在的容器：{migration.Key}");
            }
        }
    }
}
