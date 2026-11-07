using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrameworkAbstractions.Declarative;

/// <summary>声明定位状态码（受控词；异常消息/日志前缀即码）。</summary>
public static class DeclarationLocationCodes
{
    /// <summary>布局真源缺失或不可读。</summary>
    public const string LayoutUnavailable = "decl.layout-unavailable";

    /// <summary>布局真源结构非法。</summary>
    public const string LayoutInvalid = "decl.layout-invalid";

    /// <summary>角色未在布局中登记。</summary>
    public const string RoleUnknown = "decl.role-unknown";

    /// <summary>逻辑名非法（空 / 含非法字符）。</summary>
    public const string NameInvalid = "decl.name-invalid";

    /// <summary>角色目录集内未命中。</summary>
    public const string NotFound = "decl.not-found";

    /// <summary>同一解析层级命中多项（禁止静默取首项）。</summary>
    public const string Ambiguous = "decl.ambiguous";

    /// <summary>命中。</summary>
    public const string Resolved = "decl.resolved";
}

/// <summary>
/// 布局角色声明（数据，非逻辑）：角色稳定身份 + 当前目录集 + 命名口径。
/// <para>真源 = 声明布局 JSON（如 <c>Config\declaration-layout.json</c>）；角色目录允许未来迁移而逻辑名不变。</para>
/// </summary>
/// <param name="Name">角色名（稳定身份，如 <c>nodes</c> / <c>workflows</c>）。</param>
/// <param name="Directories">相对布局根的目录集（按优先级）。</param>
/// <param name="Recursive">是否递归枚举（通配层级使用）。</param>
/// <param name="Subdirectories">显式子目录布局（可选；如 <c>nodes\conversation\conversation.ushar</c>）。</param>
/// <param name="Patterns">显式通配枚举模式（可选；缺省按 basename 匹配）。</param>
/// <param name="Excludes">排除目录段（相对任意层级）。</param>
/// <param name="Extensions">无扩展名逻辑名的候选扩展名（按优先级）。</param>
/// <param name="Naming">命名口径（工具字段，如 <c>role-basename</c> / <c>full-path</c>）。</param>
public sealed record DeclarationRole(
    string Name,
    IReadOnlyList<string> Directories,
    bool Recursive,
    IReadOnlyList<string>? Subdirectories = null,
    IReadOnlyList<string>? Patterns = null,
    IReadOnlyList<string>? Excludes = null,
    IReadOnlyList<string>? Extensions = null,
    string? Naming = null)
{
    /// <summary>无扩展名逻辑名的候选扩展名（缺省 <c>.ushar</c>）。</summary>
    public IReadOnlyList<string> EffectiveExtensions
        => Extensions is { Count: > 0 } ? Extensions : [".ushar"];
}

/// <summary>
/// 声明布局（角色清单）。<see cref="Parse"/> 对缺失/非法结构一律抛（不返回内置缺省）。
/// </summary>
public sealed record DeclarationLayoutSpec(IReadOnlyList<DeclarationRole> Roles)
{
    /// <summary>解析布局 JSON；结构非法 ⇒ <see cref="DeclarationLayoutException"/>（fail-fast）。</summary>
    public static DeclarationLayoutSpec Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutInvalid}: 布局内容为空（禁以缺省布局冒充权威）");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutInvalid}: 布局 JSON 解析失败（{exception.Message}）", exception);
        }

        if (root is not JsonObject obj)
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutInvalid}: 布局根须为对象");

        var rolesNode = obj["roles"];
        if (rolesNode is not JsonArray rolesArray || rolesArray.Count == 0)
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutInvalid}: 布局缺少非空 roles 数组");

        var roles = new List<DeclarationRole>(rolesArray.Count);
        for (var index = 0; index < rolesArray.Count; index++)
        {
            if (rolesArray[index] is not JsonObject roleObject)
                throw new DeclarationLayoutException(
                    $"{DeclarationLocationCodes.LayoutInvalid}: roles[{index}] 须为对象");

            var name = ReadString(roleObject, "role") ?? ReadString(roleObject, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new DeclarationLayoutException(
                    $"{DeclarationLocationCodes.LayoutInvalid}: roles[{index}] 缺 role");

            var directories = ReadStringArray(roleObject, "dirs");
            if (directories.Count == 0)
                throw new DeclarationLayoutException(
                    $"{DeclarationLocationCodes.LayoutInvalid}: 角色 '{name}' 缺非空 dirs");

            var recursive = true;
            var recursiveNode = roleObject["recursive"];
            if (recursiveNode is not null
                && (recursiveNode is not JsonValue recursiveValue
                    || !recursiveValue.TryGetValue<bool>(out recursive)))
                throw new DeclarationLayoutException(
                    $"{DeclarationLocationCodes.LayoutInvalid}: 角色 '{name}' recursive 须为布尔");
            var naming = ReadString(roleObject, "naming");
            if (!string.IsNullOrWhiteSpace(naming) && naming is not ("role-basename" or "full-path"))
                throw new DeclarationLayoutException(
                    $"{DeclarationLocationCodes.LayoutInvalid}: 角色 '{name}' naming '{naming}' 不可识别（只许 role-basename / full-path）");

            roles.Add(new DeclarationRole(
                name!,
                directories,
                recursive,
                ReadStringArray(roleObject, "subdirs"),
                ReadStringArray(roleObject, "patterns"),
                ReadStringArray(roleObject, "exclude"),
                ReadStringArray(roleObject, "extensions"),
                naming));
        }

        var duplicate = roles
            .GroupBy(role => role.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutInvalid}: 角色重复登记 '{duplicate.Key}'（布局真源须唯一）");

        return new DeclarationLayoutSpec(roles);
    }

    /// <summary>按角色名取声明（未登记 ⇒ false；调用方转 <see cref="DeclarationLocationStatus.RoleUnknown"/>）。</summary>
    public bool TryGetRole(string name, out DeclarationRole? role)
    {
        role = Roles.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return role is not null;
    }

    private static string? ReadString(JsonObject obj, string propertyName)
    {
        var node = obj[propertyName];
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string propertyName)
    {
        if (obj[propertyName] is not JsonArray array)
            return [];

        var values = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                values.Add(text.Trim());
        }

        return values;
    }
}

/// <summary>声明布局非法/不可用（显式失败；调用方不得降级为内置缺省）。</summary>
public sealed class DeclarationLayoutException : InvalidOperationException
{
    public DeclarationLayoutException(string message)
        : base(message)
    {
    }

    public DeclarationLayoutException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>声明定位状态。</summary>
public enum DeclarationLocationStatus
{
    /// <summary>已命中唯一文件。</summary>
    Resolved,

    /// <summary>布局真源缺失/不可读。</summary>
    LayoutUnavailable,

    /// <summary>布局真源结构非法。</summary>
    LayoutInvalid,

    /// <summary>角色未登记。</summary>
    RoleUnknown,

    /// <summary>逻辑名非法。</summary>
    NameInvalid,

    /// <summary>未命中。</summary>
    NotFound,

    /// <summary>同层多命中（禁止静默取首项）。</summary>
    Ambiguous,
}

/// <summary>
/// 声明定位结果（**显式失败**：任何失败都携带状态码 + 已尝试路径，不返回 <c>null</c> 让调用方猜）。
/// </summary>
/// <param name="Role">角色名。</param>
/// <param name="LogicalName">逻辑名（未加扩展名的调用方输入）。</param>
/// <param name="Status">定位状态。</param>
/// <param name="Path">命中路径（仅 <see cref="DeclarationLocationStatus.Resolved"/> 非空）。</param>
/// <param name="Candidates">同层候选（歧义时为全部命中；其余为空）。</param>
/// <param name="Tried">已尝试路径（按解析顺序；用于诊断）。</param>
/// <param name="Detail">失败/命中原因（含解析策略名）。</param>
/// <param name="Strategy">命中策略（<c>declared-path</c> / <c>flat</c> / <c>subdir-same-name</c> / <c>wildcard</c>）。</param>
public sealed record DeclarationLocation(
    string Role,
    string LogicalName,
    DeclarationLocationStatus Status,
    string? Path,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> Tried,
    string Detail,
    string? Strategy = null)
{
    /// <summary>是否命中。</summary>
    public bool IsResolved => Status == DeclarationLocationStatus.Resolved;

    /// <summary>是否失败（含未命中与歧义）。</summary>
    public bool IsFailure => !IsResolved;

    /// <summary>状态码（日志/门禁可直接输出）。</summary>
    public string Code => Status switch
    {
        DeclarationLocationStatus.Resolved => DeclarationLocationCodes.Resolved,
        DeclarationLocationStatus.LayoutUnavailable => DeclarationLocationCodes.LayoutUnavailable,
        DeclarationLocationStatus.LayoutInvalid => DeclarationLocationCodes.LayoutInvalid,
        DeclarationLocationStatus.RoleUnknown => DeclarationLocationCodes.RoleUnknown,
        DeclarationLocationStatus.NameInvalid => DeclarationLocationCodes.NameInvalid,
        DeclarationLocationStatus.Ambiguous => DeclarationLocationCodes.Ambiguous,
        _ => DeclarationLocationCodes.NotFound,
    };

    /// <summary>失败即抛（新调用方用；旧调用方可用 <see cref="IsFailure"/> 自行可见化）。</summary>
    public DeclarationLocation Require()
    {
        if (IsFailure)
            throw new InvalidOperationException($"{Code}: {Detail}（role={Role}, name={LogicalName}）");
        return this;
    }
}

/// <summary>
/// 声明文档定位内核（**路径权威单源** + 多布局解析）。
/// <para>
/// 动机：声明文件的物理布局会迁移（扁平 / 同名子目录 / 通配枚举），而逻辑身份（role + basename）稳定；
/// 把布局差异收敛到 <see cref="DeclarationLayoutSpec"/>，调用方禁止再手拼 <c>Config\nodes\x.ushar</c>。
/// </para>
/// <para>
/// 解析顺序固定：<c>declared-path</c>（逻辑名含分隔符/布局声明 full-path）→ <c>flat</c> →
/// <c>subdir-same-name</c> → <c>declared-subdir</c> → <c>wildcard</c>。
/// **同层多命中 = <see cref="DeclarationLocationStatus.Ambiguous"/>**（禁止按枚举顺序静默取首项）。
/// </para>
/// <para>
/// 与 <see cref="DeclarativeFileLoader"/> 组合：定位只回答「哪个文件」，装载只回答「内容是否合法」；
/// 定位失败不返回 <c>null</c>，装载失败抛（两者均不静默）。
/// </para>
/// </summary>
public static class DeclarationDocumentLocator
{
    private const string StrategyDeclaredPath = "declared-path";
    private const string StrategyFlat = "flat";
    private const string StrategySubdirSameName = "subdir-same-name";
    private const string StrategyDeclaredSubdir = "declared-subdir";
    private const string StrategyWildcard = "wildcard";

    /// <summary>装载布局真源；缺失/不可读/非法一律抛（fail-closed）。</summary>
    public static DeclarationLayoutSpec LoadLayout(string layoutPath)
    {
        if (string.IsNullOrWhiteSpace(layoutPath))
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutUnavailable}: 布局路径为空");

        if (!File.Exists(layoutPath))
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutUnavailable}: 布局真源缺失 {layoutPath}");

        string text;
        try
        {
            text = File.ReadAllText(layoutPath);
        }
        catch (Exception exception)
        {
            throw new DeclarationLayoutException(
                $"{DeclarationLocationCodes.LayoutUnavailable}: 布局不可读 {layoutPath}（{exception.Message}）", exception);
        }

        return DeclarationLayoutSpec.Parse(text);
    }

    /// <summary>
    /// 按角色 + 逻辑名解析声明文件（多布局；失败显式返回状态，不返回 <c>null</c>）。
    /// </summary>
    /// <param name="layout">布局真源（<see cref="LoadLayout"/> / <see cref="DeclarationLayoutSpec.Parse"/>）。</param>
    /// <param name="role">角色名。</param>
    /// <param name="logicalName">逻辑名（可带扩展名；如 <c>conversation</c> / <c>history-dispatch.rules.ushar</c>）。</param>
    /// <param name="root">布局根（绝对目录）。</param>
    public static DeclarationLocation Resolve(
        DeclarationLayoutSpec layout,
        string role,
        string logicalName,
        string root)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var tried = new List<string>();
        var trimmedName = logicalName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Fail(role, logicalName, DeclarationLocationStatus.NameInvalid, tried,
                $"{DeclarationLocationCodes.NameInvalid}: 逻辑名为空");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return Fail(role, logicalName, DeclarationLocationStatus.LayoutUnavailable, tried,
                $"{DeclarationLocationCodes.LayoutUnavailable}: 布局根为空");
        }

        if (!layout.TryGetRole(role, out var scope) || scope is null)
        {
            return Fail(role, trimmedName, DeclarationLocationStatus.RoleUnknown, tried,
                $"{DeclarationLocationCodes.RoleUnknown}: 角色 '{role}' 未在布局中登记");
        }

        var rootFull = Path.GetFullPath(root);
        var variants = NameVariants(scope, trimmedName);

        // 1) 逻辑名含分隔符 / 布局声明 full-path：先按显式路径尝试（逻辑名即相对根的路径）。
        if (trimmedName.Contains('\\') || trimmedName.Contains('/')
            || string.Equals(scope.Naming, "full-path", StringComparison.Ordinal))
        {
            var hit = ResolveCandidates([Path.Combine(rootFull, trimmedName)], tried);
            if (hit.Status == DeclarationLocationStatus.Ambiguous)
                return Ambiguous(role, trimmedName, hit.Candidates, tried, StrategyDeclaredPath);
            if (hit.Status == DeclarationLocationStatus.Resolved)
                return Resolved(role, trimmedName, hit.Path!, StrategyDeclaredPath, tried);
        }

        // 2) 扁平：<root>/<dir>/<name>；同层 = 全部声明目录，多命中 ⇒ 歧义。
        var flatHits = new List<string>();
        foreach (var directory in scope.Directories)
        {
            var dirFull = Path.Combine(rootFull, directory);
            CollectCandidates(variants.Select(variant => Path.Combine(dirFull, variant)), tried, flatHits);
        }

        var flat = Decide(flatHits);
        if (flat.Status == DeclarationLocationStatus.Ambiguous)
            return Ambiguous(role, trimmedName, flat.Candidates, tried, StrategyFlat);
        if (flat.Status == DeclarationLocationStatus.Resolved)
            return Resolved(role, trimmedName, flat.Path!, StrategyFlat, tried);

        // 3) 同名子目录：<root>/<dir>/<basename>/<name>（同层 = 全部声明目录）。
        var baseName = BaseNameOf(trimmedName);
        var sameNameHits = new List<string>();
        foreach (var directory in scope.Directories)
        {
            var dirFull = Path.Combine(rootFull, directory, baseName);
            CollectCandidates(variants.Select(variant => Path.Combine(dirFull, variant)), tried, sameNameHits);
        }

        var sameName = Decide(sameNameHits);
        if (sameName.Status == DeclarationLocationStatus.Ambiguous)
            return Ambiguous(role, trimmedName, sameName.Candidates, tried, StrategySubdirSameName);
        if (sameName.Status == DeclarationLocationStatus.Resolved)
            return Resolved(role, trimmedName, sameName.Path!, StrategySubdirSameName, tried);

        // 4) 显式子目录：<root>/<dir>/<sub>/<name>（布局声明 subdirs；不猜目录名）。
        var declaredHits = new List<string>();
        foreach (var subdirectory in scope.Subdirectories ?? [])
        {
            foreach (var directory in scope.Directories)
            {
                var dirFull = Path.Combine(rootFull, directory, subdirectory);
                CollectCandidates(variants.Select(variant => Path.Combine(dirFull, variant)), tried, declaredHits);
            }
        }

        var declared = Decide(declaredHits);
        if (declared.Status == DeclarationLocationStatus.Ambiguous)
            return Ambiguous(role, trimmedName, declared.Candidates, tried, StrategyDeclaredSubdir);
        if (declared.Status == DeclarationLocationStatus.Resolved)
            return Resolved(role, trimmedName, declared.Path!, StrategyDeclaredSubdir, tried);

        // 5) 通配枚举：目录（按 recursive）内按 basename 匹配（可用布局 patterns 收窄）。
        var wildcardHits = new List<string>();
        foreach (var directory in scope.Directories)
        {
            var dirFull = Path.Combine(rootFull, directory);
            if (!Directory.Exists(dirFull))
            {
                tried.Add(dirFull);
                continue;
            }

            wildcardHits.AddRange(EnumerateMatches(scope, dirFull, baseName, trimmedName, rootFull));
        }

        if (wildcardHits.Count > 1)
        {
            return Ambiguous(role, trimmedName, wildcardHits, tried, StrategyWildcard);
        }

        if (wildcardHits.Count == 1)
            return Resolved(role, trimmedName, wildcardHits[0], StrategyWildcard, tried);

        return Fail(role, trimmedName, DeclarationLocationStatus.NotFound, tried,
            $"{DeclarationLocationCodes.NotFound}: 角色 '{role}' 目录集内未命中 '{trimmedName}'（已尝试 {tried.Count} 个候选）");
    }

    /// <summary>按角色枚举全部候选文件（通配面；排序稳定，供对账/覆盖率统计用）。</summary>
    public static DeclarationEnumeration Enumerate(
        DeclarationLayoutSpec layout,
        string role,
        string root,
        IReadOnlyList<string>? patterns = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var tried = new List<string>();
        if (!layout.TryGetRole(role, out var scope) || scope is null)
        {
            return new DeclarationEnumeration(
                DeclarationLocationStatus.RoleUnknown, [],
                $"{DeclarationLocationCodes.RoleUnknown}: 角色 '{role}' 未在布局中登记", tried);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return new DeclarationEnumeration(
                DeclarationLocationStatus.LayoutUnavailable, [],
                $"{DeclarationLocationCodes.LayoutUnavailable}: 布局根为空", tried);
        }

        var files = new List<string>();
        foreach (var directory in scope.Directories)
        {
            var dirFull = Path.Combine(Path.GetFullPath(root), directory);
            if (!Directory.Exists(dirFull))
            {
                tried.Add(dirFull);
                continue;
            }

            files.AddRange(EnumerateAll(scope, dirFull, Path.GetFullPath(root), patterns));
        }

        files.Sort(StringComparer.Ordinal);
        return new DeclarationEnumeration(DeclarationLocationStatus.Resolved, files, "枚举完成", tried);
    }

    private static IReadOnlyList<string> NameVariants(DeclarationRole scope, string logicalName)
    {
        // 逻辑名已带扩展名（含多段扩展名，如 history-dispatch.rules.ushar）⇒ 原样保留，不再追加。
        var fileName = Path.GetFileName(logicalName);
        if (fileName.Contains('.'))
            return [fileName];

        return scope.EffectiveExtensions.Select(extension => fileName + NormalizeExtension(extension)).ToArray();
    }

    private static string NormalizeExtension(string extension)
        => extension.StartsWith('.') ? extension : "." + extension;

    private static string BaseNameOf(string logicalName)
    {
        var fileName = Path.GetFileName(logicalName);
        var extensionIndex = fileName.IndexOf('.');
        return extensionIndex > 0 ? fileName[..extensionIndex] : fileName;
    }

    private static (DeclarationLocationStatus Status, string? Path, IReadOnlyList<string> Candidates) ResolveCandidates(
        IReadOnlyList<string> candidates,
        List<string> tried)
    {
        var hits = new List<string>();
        CollectCandidates(candidates, tried, hits);
        return Decide(hits);
    }

    private static void CollectCandidates(
        IEnumerable<string> candidates,
        List<string> tried,
        List<string> hits)
    {
        foreach (var candidate in candidates)
        {
            if (!tried.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                tried.Add(candidate);

            if (File.Exists(candidate) && !hits.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                hits.Add(candidate);
        }
    }

    private static (DeclarationLocationStatus Status, string? Path, IReadOnlyList<string> Candidates) Decide(
        IReadOnlyList<string> hits)
        => hits.Count switch
        {
            0 => (DeclarationLocationStatus.NotFound, null, []),
            1 => (DeclarationLocationStatus.Resolved, hits[0], []),
            _ => (DeclarationLocationStatus.Ambiguous, null, hits),
        };

    private static IEnumerable<string> EnumerateMatches(
        DeclarationRole scope,
        string directory,
        string baseName,
        string logicalName,
        string rootFull)
    {
        var patterns = scope.Patterns is { Count: > 0 } ? scope.Patterns : null;
        var fileName = Path.GetFileName(logicalName);
        foreach (var file in EnumerateAll(scope, directory, rootFull, patterns))
        {
            var candidateName = Path.GetFileName(file);
            var matches = patterns is null
                ? string.Equals(Path.GetFileNameWithoutExtension(candidateName), baseName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(candidateName, fileName, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(Path.GetFileNameWithoutExtension(candidateName), baseName, StringComparison.OrdinalIgnoreCase);

            if (matches)
                yield return file;
        }
    }

    private static IReadOnlyList<string> EnumerateAll(
        DeclarationRole scope,
        string directory,
        string rootFull,
        IReadOnlyList<string>? patterns)
    {
        var files = new List<string>();
        var exclude = new HashSet<string>(scope.Excludes ?? [], StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return files;

        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (exclude.Contains(name))
                    continue;

                if (Directory.Exists(entry))
                {
                    if (scope.Recursive)
                        pending.Push(entry);
                    continue;
                }

                if (patterns is { Count: > 0 } && !patterns.Any(pattern => GlobMatches(name, pattern)))
                    continue;

                files.Add(Path.GetFullPath(entry));
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static bool GlobMatches(string fileName, string pattern)
    {
        // 极简通配（* 与 ?）：布局通配只用于收窄枚举面，不引入第三方 glob 语义。
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static DeclarationLocation Resolved(
        string role, string logicalName, string path, string strategy, IReadOnlyList<string> tried)
        => new(role, logicalName, DeclarationLocationStatus.Resolved, path, [], tried,
            $"{DeclarationLocationCodes.Resolved}: 命中（策略 {strategy}）", strategy);

    private static DeclarationLocation Ambiguous(
        string role, string logicalName, IReadOnlyList<string> candidates, IReadOnlyList<string> tried, string strategy)
        => new(role, logicalName, DeclarationLocationStatus.Ambiguous, null, candidates, tried,
            $"{DeclarationLocationCodes.Ambiguous}: 同层命中 {candidates.Count} 项（策略 {strategy}）；禁止静默取首项", strategy);

    private static DeclarationLocation Fail(
        string role, string logicalName, DeclarationLocationStatus status, IReadOnlyList<string> tried, string detail)
        => new(role, logicalName, status, null, [], tried, detail);
}

/// <summary>角色枚举结果（失败显式；不返回空集让调用方误当「无需对账」）。</summary>
public sealed record DeclarationEnumeration(
    DeclarationLocationStatus Status,
    IReadOnlyList<string> Files,
    string Detail,
    IReadOnlyList<string> Tried)
{
    public bool IsResolved => Status == DeclarationLocationStatus.Resolved;
}
