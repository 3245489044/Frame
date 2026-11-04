#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 视图程序目录（编译产物唯一读面）：<c>view-programs.generated.json</c> → <see cref="UiViewBlueprint"/>。
/// <para>单一来源：声明面（区域/视图/loc 反转）由 <c>Generate-ViewPrograms.ps1</c> 合并生成；本类只读，不推导、
/// 不回退第二份视图表（缺失 ⇒ 显式诊断 + <see cref="Require"/> 抛，禁静默双根）。</para>
/// </summary>
public sealed class UiViewBlueprintCatalog
{
    private readonly Dictionary<string, UiViewBlueprint> _byKey;

    private UiViewBlueprintCatalog(
        string rootName, string fingerprint, IReadOnlyDictionary<string, UiViewBlueprint> views)
    {
        RootName = rootName;
        Fingerprint = fingerprint;
        _byKey = new Dictionary<string, UiViewBlueprint>(views, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>页面视图宿主名（根子集投影的写目标）。</summary>
    public string RootName { get; }

    /// <summary>产物指纹（并入稳定指纹：改视图程序即刷新）。</summary>
    public string Fingerprint { get; }

    /// <summary>视图数。</summary>
    public int Count => _byKey.Count;

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

    /// <summary>解析产物 JSON（结构非法 ⇒ 抛：判据不可判时不得放行）。</summary>
    public static UiViewBlueprintCatalog FromJson(string json, string sourceName = "view-programs")
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("视图程序产物为空。", nameof(json));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rootName = Prop(root, "rootName");
        if (string.IsNullOrWhiteSpace(rootName)) rootName = UiViewSkeleton.DefaultRootName;

        var views = new Dictionary<string, UiViewBlueprint>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("views", out var viewsElement) && viewsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in viewsElement.EnumerateArray())
            {
                var viewKey = Prop(item, "viewKey");
                if (string.IsNullOrWhiteSpace(viewKey))
                    throw new InvalidOperationException($"{sourceName}: 视图条目缺 viewKey");

                var containers = new List<UiViewContainer>();
                if (item.TryGetProperty("containers", out var containerArray) && containerArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var container in containerArray.EnumerateArray())
                    {
                        var key = Prop(container, "key");
                        if (string.IsNullOrWhiteSpace(key))
                            throw new InvalidOperationException($"{sourceName}: 视图 '{viewKey}' 容器缺 key");
                        var owner = Prop(container, "ownerView") ?? viewKey!;
                        var role = string.Equals(Prop(container, "role"), "stream", StringComparison.OrdinalIgnoreCase)
                            ? UiViewContainerRole.Stream
                            : UiViewContainerRole.Region;
                        containers.Add(new UiViewContainer(key!.Trim(), owner!.Trim(), role));
                    }
                }

                var migrations = new List<UiViewStateMigration>();
                foreach (var name in new[] { "stateMigration", "stateMigrations" })
                {
                    if (!item.TryGetProperty(name, out var migrationArray) || migrationArray.ValueKind != JsonValueKind.Array) continue;
                    foreach (var migration in migrationArray.EnumerateArray())
                    {
                        var kind = Prop(migration, "kind");
                        var key = Prop(migration, "key");
                        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(key))
                            throw new InvalidOperationException($"{sourceName}: 视图 '{viewKey}' 状态迁移缺 kind/key");
                        migrations.Add(new UiViewStateMigration(kind!.Trim(), key!.Trim()));
                    }
                }

                var lifecycle = string.Equals(Prop(item, "lifecycle"), "persistent", StringComparison.OrdinalIgnoreCase)
                    ? UiViewLifecycle.Persistent
                    : UiViewLifecycle.OnDemand;

                views[viewKey!] = new UiViewBlueprint(viewKey!.Trim(), lifecycle, containers, migrations);
            }
        }

        if (views.Count == 0)
            throw new InvalidOperationException($"{sourceName}: 视图集为空（view-programs 未产出任何视图）");

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var catalog = new UiViewBlueprintCatalog(rootName!.Trim(), fingerprint, views);
        catalog.Validate(sourceName);
        catalog.EnsureReady(sourceName);
        return catalog;
    }

    /// <summary>取视图程序；未声明 ⇒ 抛（fail-fast：新增视图必须先在声明面登记）。</summary>
    public UiViewBlueprint Require(string viewKey)
        => TryGet(viewKey, out var blueprint)
            ? blueprint
            : throw new InvalidOperationException($"视图程序未声明：{viewKey}（view-programs.generated.json）");

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
    /// 结构校验（运行期镜像）：容器键在视图内唯一、所有者与视图一致、至多一个流容器、迁移键 ⊆ 容器键。
    /// <para>判据单源 = <c>Frame\PowerShellScript\Share\Ui\Generate-ViewPrograms.ps1</c>（G1–G4）；
    /// 本方法是同一判据的运行期镜像（产物被门禁逐键比对 ⇒ 抵达此处的产物必已通过生成器）。
    /// 违反 ⇒ 抛（判据载体 = 声明结构事实，非执行者叙述）。</para>
    /// </summary>
    public void Validate(string sourceName = "view-programs")
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

    private static string? Prop(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}


