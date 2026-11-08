#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 载体映射声明（生成物 <c>surface-map.generated.json</c>；真源 = <c>Config\ui\ui.ushar</c> 的 <c>ui_part_*</c> 投影）。
/// <para>
/// 结构：<c>roles[key][backendId] = { prop: value }</c>。载体名只出现在声明面，
/// 代码侧按后端 id 取值（零载体字面量）。
/// </para>
/// </summary>
public sealed class SurfaceMapDeclaration
{
    /// <summary>空声明（未装配）：后端一律走可见降级。</summary>
    public static readonly SurfaceMapDeclaration Empty = new(
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal),
        declared: false);

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _roles;

    private SurfaceMapDeclaration(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> roles,
        bool declared)
    {
        _roles = roles;
        IsDeclared = declared;
    }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>声明中出现的后端 id 集（联合）。</summary>
    public IReadOnlyCollection<string> BackendIds
    {
        get
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _roles.Values)
            {
                foreach (var backend in entry.Keys) ids.Add(backend);
            }

            return ids;
        }
    }

    /// <summary>取某后端在某键上的属性（未声明键 ⇒ <c>__default</c> ⇒ null）。</summary>
    /// <param name="backendId">后端 id。</param>
    /// <param name="key">角色/单元键。</param>
    public IReadOnlyDictionary<string, string>? Props(string backendId, string key)
    {
        if (key is not null && _roles.TryGetValue(key, out var entry) && entry.TryGetValue(backendId, out var props))
        {
            return props;
        }

        return _roles.TryGetValue(DefaultKey, out var fallback) && fallback.TryGetValue(backendId, out var defaults)
            ? defaults
            : null;
    }

    /// <summary>取某后端在某键上的属性（**不回退缺省键**：键不存在 ⇒ false）。</summary>
    /// <param name="backendId">后端 id。</param>
    /// <param name="key">角色/单元键。</param>
    /// <param name="props">属性表（未声明 ⇒ null）。</param>
    public bool TryProps(string backendId, string key, out IReadOnlyDictionary<string, string>? props)
    {
        if (key is not null && _roles.TryGetValue(key, out var entry) && entry.TryGetValue(backendId, out props))
        {
            return true;
        }

        props = null;
        return false;
    }

    /// <summary>缺省键（未声明键的兜底来源）。</summary>
    public const string DefaultKey = "__default";

    /// <summary>解析声明 JSON（不可解析 ⇒ 抛）。</summary>
    /// <param name="json">声明文本。</param>
    public static SurfaceMapDeclaration Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var roles = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(
            StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var role in rolesElement.EnumerateObject())
            {
                if (role.Value.ValueKind != JsonValueKind.Object) continue;
                var carriers = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
                foreach (var carrier in role.Value.EnumerateObject())
                {
                    if (carrier.Value.ValueKind != JsonValueKind.Object) continue;
                    var props = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var property in carrier.Value.EnumerateObject())
                    {
                        props[property.Name] = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.Number => property.Value.GetRawText(),
                            _ => string.Empty,
                        };
                    }

                    carriers[carrier.Name] = props;
                }

                roles[role.Name] = carriers;
            }
        }

        return new SurfaceMapDeclaration(roles, declared: roles.Count > 0);
    }
}
