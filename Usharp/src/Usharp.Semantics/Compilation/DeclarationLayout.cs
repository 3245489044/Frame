using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Usharp.Semantics;

/// <summary>声明面逻辑名策略：物理目录如何投影为逻辑名（声明库键 / 诊断归因键）。</summary>
public enum DeclarationNaming
{
    /// <summary>逻辑名 = 根相对路径（去扩展名）；目录移动即改键（旧口径）。</summary>
    FullPath = 0,

    /// <summary>逻辑名 = <c>&lt;role&gt;/&lt;basename&gt;</c>；模块子目录不参与键（推荐）。</summary>
    RoleBasename = 1,
}

/// <summary>
/// 一个声明角色（role）的物理装载范围：目录集 / 递归口径 / 排除项 / 命名策略。
/// <para>角色是**稳定身份**（<c>workflows</c> / <c>nodes</c> / <c>llm</c>…），目录只是它的当前位置；
/// 角色与目录解耦后，模块化子目录不再改变逻辑名、库键与诊断键。</para>
/// </summary>
/// <param name="Role">角色名（逻辑名前缀，如 <c>workflows</c>）。</param>
/// <param name="Directories">相对配置根的目录集（缺失静默跳过）。</param>
/// <param name="Recursive">是否递归子目录。</param>
/// <param name="Exclude">排除项（按路径段匹配，如 <c>_deprecated</c>）。</param>
/// <param name="Naming">逻辑名策略。</param>
public sealed record DeclarationScope(
    string Role,
    IReadOnlyList<string> Directories,
    bool Recursive = false,
    IReadOnlyList<string>? Exclude = null,
    DeclarationNaming Naming = DeclarationNaming.FullPath);

/// <summary>一个闭包组成：参与的角色 + 根级文件（角色目录之外的单文件声明）。</summary>
/// <param name="Name">闭包号（<c>core</c> / <c>toolface</c> / <c>ui</c> / <c>gather</c>）。</param>
/// <param name="Roles">参与的角色名（按声明序）。</param>
/// <param name="RootFiles">根级文件名（相对配置根；逻辑名 = 去扩展名的相对路径）。</param>
public sealed record DeclarationBundle(
    string Name,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> RootFiles);

/// <summary>
/// 声明面布局（**数据驱动的单一布局真源**）：把「角色 → 目录/文件 → 命名策略 → 闭包组成」外置为可配置描述，
/// 取代 Frame 内按固定目录名硬编码的闭包工厂。
/// <para>
/// 缺省（<see cref="Default"/>）= 当前仓库既有形态的等价描述（行为零回归）；
/// 配置根存在 <c>declaration-layout.json</c> 时以其为准（模块化目录/新增角色只改数据，不改代码）。
/// </para>
/// <para>本类零领域逻辑：只做「描述 → 闭包规格 / 逻辑名」，编译与校验仍归 <see cref="UsharpCompilation"/>。</para>
/// </summary>
public sealed class DeclarationLayout
{
    /// <summary>布局描述文件名（位于配置根）。</summary>
    public const string FileName = "declaration-layout.json";

    /// <summary>布局描述 schema 哨兵（防手改/漂移）。</summary>
    public const string Schema = "declaration-layout-v1";

    private static readonly Lazy<DeclarationLayout> DefaultLazy = new(BuildDefault);

    /// <summary>内置缺省布局（等价当前仓库形态）。</summary>
    public static DeclarationLayout Default => DefaultLazy.Value;

    private readonly Dictionary<string, DeclarationBundle> _bundles;
    private readonly Dictionary<string, DeclarationScope> _scopes;

    /// <param name="scopes">角色范围集（按声明序）。</param>
    /// <param name="bundles">闭包组成（按声明序）。</param>
    public DeclarationLayout(IReadOnlyList<DeclarationScope> scopes, IReadOnlyList<DeclarationBundle> bundles)
    {
        Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        Bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));

        _bundles = new Dictionary<string, DeclarationBundle>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in Bundles) _bundles[bundle.Name] = bundle;

        _scopes = new Dictionary<string, DeclarationScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in Scopes) _scopes[scope.Role] = scope;
    }

    /// <summary>角色范围集（声明序）。</summary>
    public IReadOnlyList<DeclarationScope> Scopes { get; }

    /// <summary>闭包组成（声明序）。</summary>
    public IReadOnlyList<DeclarationBundle> Bundles { get; }

    /// <summary>按配置根加载布局：存在 <c>declaration-layout.json</c> 即以其为准，缺失/非法回落 <see cref="Default"/>（诊断可见）。</summary>
    /// <param name="configRoot">配置根目录。</param>
    /// <param name="diagnostics">诊断（缺失/非法原因；正常为空）。</param>
    public static DeclarationLayout FromRoot(string configRoot, out IReadOnlyList<string> diagnostics)
    {
        var diag = new List<string>();
        diagnostics = diag;
        if (string.IsNullOrWhiteSpace(configRoot)) return Default;

        var path = Path.Combine(configRoot, FileName);
        if (!File.Exists(path)) return Default;

        var loaded = TryLoad(path, out var layoutDiagnostics);
        if (layoutDiagnostics.Count > 0) diag.AddRange(layoutDiagnostics);
        return loaded ?? Default;
    }

    /// <summary>按配置根加载布局（诊断丢弃的便捷重载；需要可见化请用带 out 的重载）。</summary>
    /// <param name="configRoot">配置根目录。</param>
    public static DeclarationLayout FromRoot(string configRoot) => FromRoot(configRoot, out _);

    /// <summary>读取布局描述（非法 ⇒ null + 诊断；不抛）。</summary>
    /// <param name="path">布局描述文件绝对路径。</param>
    /// <param name="diagnostics">诊断（非法原因）。</param>
    public static DeclarationLayout? TryLoad(string path, out IReadOnlyList<string> diagnostics)
    {
        var diag = new List<string>();
        diagnostics = diag;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                diag.Add($"declaration-layout: 根节点不是对象（{path}）");
                return null;
            }

            if (doc.RootElement.TryGetProperty("schema", out var schema)
                && !string.Equals(schema.GetString(), Schema, StringComparison.Ordinal))
            {
                diag.Add($"declaration-layout: schema 不匹配（期望 {Schema}，实际 {schema.GetString()}）");
                return null;
            }

            var scopes = new List<DeclarationScope>();
            if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var name = role.TryGetProperty("role", out var r) ? r.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var dirs = ReadStringArray(role, "dirs");
                    var recursive = role.TryGetProperty("recursive", out var rec) && rec.ValueKind == JsonValueKind.True;
                    var naming = role.TryGetProperty("naming", out var nm)
                        && string.Equals(nm.GetString(), "role-basename", StringComparison.OrdinalIgnoreCase)
                        ? DeclarationNaming.RoleBasename
                        : DeclarationNaming.FullPath;
                    var exclude = ReadStringArray(role, "exclude");
                    scopes.Add(new DeclarationScope(name!, dirs, recursive, exclude, naming));
                }
            }

            if (scopes.Count == 0)
            {
                diag.Add("declaration-layout: roles 为空（判据无标的，回落内置布局）");
                return null;
            }

            var bundles = new List<DeclarationBundle>();
            if (doc.RootElement.TryGetProperty("bundles", out var bundleNode) && bundleNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in bundleNode.EnumerateObject())
                {
                    var roleNames = ReadStringArray(property.Value, "roles");
                    var rootFiles = ReadStringArray(property.Value, "rootFiles");
                    bundles.Add(new DeclarationBundle(property.Name, roleNames, rootFiles));
                }
            }

            if (bundles.Count == 0)
            {
                diag.Add("declaration-layout: bundles 为空（回落内置布局）");
                return null;
            }

            // 悬空角色检测：bundle 引用了未声明的角色 ⇒ 非法（禁静默跳过的空闭包）。
            foreach (var bundle in bundles)
            {
                foreach (var roleName in bundle.Roles)
                {
                    if (scopes.Any(s => string.Equals(s.Role, roleName, StringComparison.OrdinalIgnoreCase))) continue;
                    diag.Add($"declaration-layout: bundle '{bundle.Name}' 引用了未声明角色 '{roleName}'");
                }
            }

            return diag.Count == 0 ? new DeclarationLayout(scopes, bundles) : null;
        }
        catch (Exception ex)
        {
            diag.Add($"declaration-layout: 解析失败 {path}（{ex.Message}）");
            return null;
        }
    }

    /// <summary>取角色的装载范围（未声明 ⇒ null）。</summary>
    /// <param name="role">角色名。</param>
    public DeclarationScope? ScopeOf(string role)
        => !string.IsNullOrWhiteSpace(role) && _scopes.TryGetValue(role, out var scope) ? scope : null;

    /// <summary>按闭包号生成闭包规格（未声明 ⇒ 抛；闭包号拼错即缺陷）。</summary>
    /// <param name="bundleId">闭包号（<c>core</c> / <c>toolface</c> / <c>ui</c> / <c>gather</c>）。</param>
    /// <param name="root">配置根。</param>
    public DeclarationClosureSpec Spec(string bundleId, string root)
    {
        if (string.IsNullOrWhiteSpace(bundleId)) throw new ArgumentException("闭包号不得为空。", nameof(bundleId));
        if (!_bundles.TryGetValue(bundleId, out var bundle))
            throw new InvalidOperationException($"declaration-layout: 未登记闭包 '{bundleId}'。");

        var scopes = new List<DeclarationScope>(bundle.Roles.Count);
        foreach (var roleName in bundle.Roles)
        {
            var scope = ScopeOf(roleName);
            if (scope is null)
                throw new InvalidOperationException($"declaration-layout: 闭包 '{bundleId}' 引用的角色 '{roleName}' 未声明。");
            scopes.Add(scope);
        }

        return new DeclarationClosureSpec(
            root,
            bundle.Roles,
            bundle.RootFiles,
            Scopes: scopes,
            Naming: DeclarationNaming.FullPath,
            Exclude: null,
            Role: bundleId);
    }

    /// <summary>
    /// 物理路径 → 逻辑名（按命中的角色范围；未命中 ⇒ 根相对路径去扩展名）。
    /// <para>角色内 <c>RoleBasename</c> ⇒ <c>&lt;role&gt;/&lt;basename&gt;</c>（模块目录不参与）；根级文件 ⇒ 相对路径。</para>
    /// </summary>
    /// <param name="physicalPath">声明文件绝对路径。</param>
    /// <param name="root">配置根。</param>
    public string ToLogicalName(string physicalPath, string root)
    {
        if (string.IsNullOrWhiteSpace(physicalPath)) return string.Empty;

        var relative = NormalizeRelative(physicalPath, root);
        var withoutExt = StripExtension(relative);

        foreach (var scope in Scopes)
        {
            if (!MatchesScope(relative, scope)) continue;
            if (scope.Naming != DeclarationNaming.RoleBasename) return withoutExt;
            return scope.Role + "/" + Path.GetFileNameWithoutExtension(physicalPath);
        }

        return withoutExt;
    }

    /// <summary>路径是否落在角色范围内（供定位器/门禁复用）。</summary>
    /// <param name="physicalPath">声明文件绝对路径。</param>
    /// <param name="root">配置根。</param>
    /// <param name="role">命中的角色（未命中 ⇒ null）。</param>
    public bool TryGetRole(string physicalPath, string root, out string? role)
    {
        role = null;
        var relative = NormalizeRelative(physicalPath, root);
        foreach (var scope in Scopes)
        {
            if (!MatchesScope(relative, scope)) continue;
            role = scope.Role;
            return true;
        }
        return false;
    }

    private static bool MatchesScope(string relative, DeclarationScope scope)
    {
        foreach (var dir in scope.Directories)
        {
            var normalized = (dir ?? string.Empty).Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) continue;
            if (string.Equals(relative, normalized, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeRelative(string path, string root)
    {
        var relative = string.IsNullOrWhiteSpace(root)
            ? path
            : Path.GetRelativePath(root, path);
        return relative.Replace('\\', '/').TrimStart('/');
    }

    private static string StripExtension(string relative)
        => relative.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase)
            ? relative[..^UsharpSourceLoader.Extension.Length]
            : relative;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) result.Add(value!);
        }
        return result;
    }

    private static DeclarationLayout BuildDefault()
    {
        // 与既有闭包工厂等价：六目录（递归 + 角色/基名命名 + 退役区排除）、UI/聚拢（顶层）、LLM（递归）、执行端画像（全路径）。
        var scopes = new List<DeclarationScope>
        {
            new("nodes", new[] { "nodes" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("workflows", new[] { "workflows" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("keyspaces", new[] { "keyspaces" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("commands", new[] { "commands" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("state", new[] { "state" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("relations", new[] { "relations" }, true, new[] { "_deprecated" }, DeclarationNaming.RoleBasename),
            new("ui", new[] { "ui" }, false, new[] { "_deprecated" }, DeclarationNaming.FullPath),
            new("gather", new[] { "gather" }, false, new[] { "_deprecated" }, DeclarationNaming.FullPath),
            new("llm", new[] { "llm" }, true, new[] { "prompts", "_deprecated" }, DeclarationNaming.RoleBasename),
            new("agents", new[] { "agents" }, true, new[] { "_deprecated" }, DeclarationNaming.FullPath),
        };

        var bundles = new List<DeclarationBundle>
        {
            new("core", new[] { "nodes", "workflows", "keyspaces", "commands", "state", "relations" },
                new[] { "prompt-segments.ushar" }),
            new("toolface", new[] { "keyspaces" }, new[] { "tool-face.ushar" }),
            new("ui", new[] { "ui" }, Array.Empty<string>()),
            new("gather", new[] { "gather" }, Array.Empty<string>()),
        };

        return new DeclarationLayout(scopes, bundles);
    }
}
