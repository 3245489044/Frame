using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Usharp.Semantics;

/// <summary>
/// 声明闭包规格（**唯一文档集声明形态**）：根 + 子目录 + 根级文件。
/// <para>
/// 领域语义（哪些目录属于 bundle / 工具面 / 关系面）由**组合根**用命名工厂声明；
/// 内核只负责「按规格取文、算指纹」，不再由某个 Builder 私藏目录清单。
/// </para>
/// </summary>
/// <param name="Root">配置根目录。</param>
/// <param name="Subdirectories">参与闭包的子目录名（缺失静默跳过）。</param>
/// <param name="RootFiles">参与闭包的根级文件名（缺失静默跳过）。</param>
/// <param name="Recursive">子目录是否递归。</param>
/// <param name="Scopes">角色范围集（数据驱动；缺省 ⇒ 由 <paramref name="Subdirectories"/>/<paramref name="Recursive"/> 派生）。</param>
/// <param name="Naming">逻辑名策略（未给 <paramref name="Scopes"/> 时生效）。</param>
/// <param name="Exclude">排除项（按路径段；未给 <paramref name="Scopes"/> 时生效）。</param>
/// <param name="Role">闭包号/角色归因（诊断用；未给 <paramref name="Scopes"/> 时作为单角色名）。</param>
public sealed record DeclarationClosureSpec(
    string Root,
    IReadOnlyList<string> Subdirectories,
    IReadOnlyList<string> RootFiles,
    bool Recursive = false,
    IReadOnlyList<DeclarationScope>? Scopes = null,
    DeclarationNaming Naming = DeclarationNaming.FullPath,
    IReadOnlyList<string>? Exclude = null,
    string? Role = null)
{
    /// <summary>冻结产物闭包（nodes/workflows/keyspaces/commands/state/relations + 根级段声明）。</summary>
    public static DeclarationClosureSpec Bundle(string root) => DeclarationLayout.Default.Spec("core", root);

    /// <summary>键空间闭包（仅 keyspaces 目录）。</summary>
    public static DeclarationClosureSpec Keyspaces(string root)
        => new(root, new[] { "keyspaces" }, Array.Empty<string>(),
            Scopes: new[] { DeclarationLayout.Default.ScopeOf("keyspaces")! },
            Role: "keyspaces");

    /// <summary>工具面闭包（tool-face.ushar + 同配置根 keyspaces 词表）。</summary>
    public static DeclarationClosureSpec ToolFace(string root) => DeclarationLayout.Default.Spec("toolface", root);

    /// <summary>关系闭包（全量 bundle 的子集；筛选在 <see cref="DeclarationClosures.Filter"/>）。</summary>
    public static DeclarationClosureSpec Relations(string root) => Bundle(root);

    /// <summary>
    /// UI 声明闭包（仅 <c>ui</c> 子目录的顶层 <c>*.ushar</c>）。
    /// <para>
    /// 独立于 <see cref="Bundle"/>：UI 声明（<c>[Space] UiLoc</c> / UI 表 / UI 记录）**不进入**共享
    /// manifest 的词表段（否则改 UI 文案即改 manifest 指纹），也保证回退只改一行。
    /// </para>
    /// </summary>
    public static DeclarationClosureSpec Ui(string root) => DeclarationLayout.Default.Spec("ui", root);

    /// <summary>
    /// 聚拢视图声明闭包（仅 <c>gather</c> 子目录）。
    /// <para>
    /// 独立于 <see cref="Bundle"/> / <see cref="Ui"/>：聚拢视图（Stage/Need/Produce）不进入共享 manifest
    /// 也不是 UI 视图，闭包隔离保证「改聚拢声明不动 UI 指纹、也不动 manifest 指纹」。
    /// </para>
    /// </summary>
    public static DeclarationClosureSpec Gather(string root) => DeclarationLayout.Default.Spec("gather", root);
}

/// <summary>
/// 已解析声明闭包：文档集 + 指纹 + 身份。**不可变**，可安全跨消费方共享。
/// </summary>
/// <param name="Identity">闭包身份（日志/缓存归因）。</param>
/// <param name="Root">配置根（指纹路径归一的基）。</param>
/// <param name="Spec">产生本闭包的规格。</param>
/// <param name="Sources">源集（路径序）。</param>
/// <param name="Fingerprint">全量语料指纹（<see cref="DeclarationArtifactFingerprint.Of"/>）。</param>
/// <param name="LogicalNames">物理路径 → 逻辑名（角色/基名策略；声明库键与诊断归因键的稳定面）。</param>
public sealed record DeclarationClosure(
    string Identity,
    string Root,
    DeclarationClosureSpec Spec,
    IReadOnlyList<UsharpSourceFile> Sources,
    string Fingerprint,
    IReadOnlyDictionary<string, string>? LogicalNames = null)
{
    /// <summary>取源文件的逻辑名（未登记 ⇒ null，调用方按根相对路径回退）。</summary>
    /// <param name="physicalPath">源文件路径。</param>
    public string? LogicalName(string physicalPath)
        => LogicalNames is not null && physicalPath is not null
           && LogicalNames.TryGetValue(physicalPath, out var name)
            ? name
            : null;
}

/// <summary>
/// 闭包解析**单一入口**：规格 → 源集 + 指纹。所有消费方（产物构建 / 图投影 / 工具面 / 关系面）
/// 一律经本类取声明文档，禁自行 <c>File.ReadAllText</c> 或自行枚举目录。
/// </summary>
public static class DeclarationClosures
{
    /// <summary>按规格解析闭包。</summary>
    /// <param name="spec">闭包规格。</param>
    public static DeclarationClosure Resolve(DeclarationClosureSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Root);

        // 数据驱动角色范围（layout）；未给则按旧口径（单一目录集 + 全局递归/命名/排除）派生。
        var scopes = spec.Scopes is { Count: > 0 }
            ? spec.Scopes
            : new[]
            {
                new DeclarationScope(
                    spec.Role ?? string.Empty,
                    spec.Subdirectories ?? Array.Empty<string>(),
                    spec.Recursive,
                    spec.Exclude,
                    spec.Naming),
            };

        var sources = new List<UsharpSourceFile>();
        var logicalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in scopes)
        {
            foreach (var directory in scope.Directories ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var fullDirectory = Path.Combine(spec.Root, directory);
                if (!Directory.Exists(fullDirectory)) continue;

                var search = scope.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var path in Directory.EnumerateFiles(fullDirectory, "*" + UsharpSourceLoader.Extension, search)
                             .OrderBy(p => p, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(spec.Root, path).Replace('\\', '/');
                    if (IsExcluded(relative, scope.Exclude)) continue;
                    if (!seen.Add(path)) continue;

                    sources.Add(new UsharpSourceFile(path, File.ReadAllText(path), null));
                    logicalNames[path] = LogicalName(path, spec.Root, scope);
                }
            }
        }

        foreach (var fileName in (spec.RootFiles ?? Array.Empty<string>())
                     .Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var path = Path.Combine(spec.Root, fileName);
            if (!File.Exists(path)) continue;
            if (!seen.Add(path)) continue;

            sources.Add(new UsharpSourceFile(path, File.ReadAllText(path), null));
            // 根级文件逻辑名 = 相对路径去扩展名（与旧口径一致；不参与角色/基名折叠）。
            logicalNames[path] = StripExtension(fileName.Replace('\\', '/'));
        }

        var fingerprint = DeclarationArtifactFingerprint.Of(sources, spec.Root);
        return new DeclarationClosure($"closure:{spec.Root}", spec.Root, spec, sources, fingerprint, logicalNames);
    }

    /// <summary>排除项按路径段匹配（如 <c>_deprecated</c> / <c>prompts</c>）。</summary>
    private static bool IsExcluded(string relativePath, IReadOnlyList<string>? exclude)
    {
        if (exclude is null || exclude.Count == 0) return false;
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in exclude)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            foreach (var segment in segments)
            {
                if (string.Equals(segment, item, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static string LogicalName(string physicalPath, string root, DeclarationScope scope)
    {
        var relative = StripExtension(Path.GetRelativePath(root, physicalPath).Replace('\\', '/'));
        if (scope.Naming != DeclarationNaming.RoleBasename || string.IsNullOrWhiteSpace(scope.Role))
            return relative;
        return scope.Role + "/" + Path.GetFileNameWithoutExtension(physicalPath);
    }

    private static string StripExtension(string relative)
        => relative.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase)
            ? relative[..^UsharpSourceLoader.Extension.Length]
            : relative;

    /// <summary>由内存文档构造闭包（测试 / 显式输入；指纹只取文件名）。</summary>
    /// <param name="documents">路径 + 文本。</param>
    /// <param name="identity">闭包身份。</param>
    public static DeclarationClosure FromDocuments(
        IEnumerable<(string Path, string Text)> documents, string identity = "explicit")
    {
        ArgumentNullException.ThrowIfNull(documents);
        var sources = documents
            .Select(d => new UsharpSourceFile(d.Path, d.Text ?? string.Empty, null))
            .ToList();
        var spec = new DeclarationClosureSpec(string.Empty, Array.Empty<string>(), Array.Empty<string>());
        return new DeclarationClosure(identity, string.Empty, spec, sources,
            DeclarationArtifactFingerprint.Of(sources));
    }

    /// <summary>由内存源集构造闭包（lib 逐文件保留；缓存指纹含 lib，避免同文本跨 lib 误命中）。</summary>
    /// <param name="sources">源集。</param>
    /// <param name="identity">闭包身份。</param>
    public static DeclarationClosure FromSources(
        IReadOnlyList<UsharpSourceFile> sources, string identity = "sources")
    {
        ArgumentNullException.ThrowIfNull(sources);
        var spec = new DeclarationClosureSpec(string.Empty, Array.Empty<string>(), Array.Empty<string>());
        var buffer = new System.Text.StringBuilder();
        foreach (var source in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            buffer.Append(source.Path).Append('|')
                  .Append(source.Lib ?? string.Empty).Append('|')
                  .Append(source.Text ?? string.Empty).Append('\n');
        }

        var fingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(buffer.ToString())))
            .ToLowerInvariant();
        return new DeclarationClosure(identity, string.Empty, spec, sources, fingerprint);
    }

    /// <summary>取子闭包（同一根、同一路径归一；用于关系面等「全量中的子集」）。</summary>
    /// <param name="closure">源闭包。</param>
    /// <param name="predicate">文档筛选判据（结构事实）。</param>
    /// <param name="identitySuffix">子闭包身份后缀。</param>
    public static DeclarationClosure Filter(
        DeclarationClosure closure, Func<UsharpSourceFile, bool> predicate, string identitySuffix)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentNullException.ThrowIfNull(predicate);
        var sources = closure.Sources.Where(predicate).ToList();
        return new DeclarationClosure(
            $"{closure.Identity}:{identitySuffix}",
            closure.Root,
            closure.Spec,
            sources,
            DeclarationArtifactFingerprint.Of(sources, string.IsNullOrWhiteSpace(closure.Root) ? null : closure.Root));
    }
}
