using System.Collections.Generic;
using System.IO;

namespace Usharp.Semantics;

/// <summary>
/// 声明库的文件实现（**现磁盘语义**，迁移期回退面）：根目录下递归 <c>*.ushar</c>，
/// 逻辑名 = 根相对路径（<c>/</c> 归一，去 <c>.ushar</c> 扩展名，如 <c>workflows/task-provision</c>），
/// <see cref="DeclarationEntry.SourcePath"/> = 真实绝对路径（诊断保真）。
/// <para>
/// 与 <see cref="UsharpSourceLoader.FromPath"/> 的目录装载同源（同一扩展名、同一路径序复现）：
/// 差异仅在「逻辑名去扩展名」—— 使文件库与数据库库在装载面**同形**（同一逻辑名键域）。
/// </para>
/// </summary>
public sealed class FileDeclarationStore : IDeclarationStore
{
    private readonly string _root;

    /// <param name="root">根目录（不存在 ⇒ <see cref="ListAsync"/> 返回空集，不禁抛）。</param>
    public FileDeclarationStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>根目录（诊断用）。</summary>
    public string Root => _root;

    /// <inheritdoc />
    public Task<IReadOnlyList<DeclarationEntry>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root)) return Task.FromResult<IReadOnlyList<DeclarationEntry>>(Array.Empty<DeclarationEntry>());

        var entries = Directory
            .EnumerateFiles(_root, "*" + UsharpSourceLoader.Extension, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new DeclarationEntry(ToLogicalName(path), File.ReadAllText(path), null, path))
            .ToList();

        return Task.FromResult<IReadOnlyList<DeclarationEntry>>(entries);
    }

    /// <inheritdoc />
    public Task<DeclarationEntry?> GetAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(logicalName)) return Task.FromResult<DeclarationEntry?>(null);

        var path = ToPhysicalPath(logicalName);
        if (!File.Exists(path)) return Task.FromResult<DeclarationEntry?>(null);

        var entry = new DeclarationEntry(Normalize(logicalName), File.ReadAllText(path), null, path);
        return Task.FromResult<DeclarationEntry?>(entry);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(logicalName) && File.Exists(ToPhysicalPath(logicalName)));
    }

    /// <inheritdoc />
    public Task PutAsync(DeclarationEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(entry.LogicalName)) return Task.CompletedTask;

        var path = ToPhysicalPath(entry.LogicalName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, entry.Text);
        return Task.CompletedTask;
    }

    /// <summary>物理路径 → 逻辑名（根相对、<c>/</c> 归一、去扩展名）。</summary>
    private string ToLogicalName(string physicalPath)
    {
        var relative = Path.GetRelativePath(_root, physicalPath).Replace('\\', '/');
        return StripExtension(relative);
    }

    /// <summary>逻辑名 → 物理路径（补 <c>.ushar</c> 扩展名，<c>/</c> → 平台分隔符）。</summary>
    private string ToPhysicalPath(string logicalName)
    {
        var name = Normalize(logicalName);
        if (!name.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase))
            name += UsharpSourceLoader.Extension;
        return Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Normalize(string logicalName) => logicalName.Trim().Replace('\\', '/');

    private static string StripExtension(string relative)
        => relative.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase)
            ? relative[..^UsharpSourceLoader.Extension.Length]
            : relative;
}
