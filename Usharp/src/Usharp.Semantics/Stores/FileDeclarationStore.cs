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
    private readonly Func<string, string?>? _logicalNameOf;

    /// <param name="root">根目录（不存在 ⇒ <see cref="ListAsync"/> 返回空集，不禁抛）。</param>
    /// <param name="logicalNameOf">逻辑名投影（物理路径 → 逻辑名；null ⇒ 根相对路径去扩展名）。
    /// 供布局化命名（<c>&lt;role&gt;/&lt;basename&gt;</c>）注入：模块子目录不改变声明库键。</param>
    public FileDeclarationStore(string root, Func<string, string?>? logicalNameOf = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _logicalNameOf = logicalNameOf;
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
            .Select(path => new DeclarationEntry(LogicalNameOf(path), File.ReadAllText(path), null, path))
            .ToList();

        return Task.FromResult<IReadOnlyList<DeclarationEntry>>(entries);
    }

    /// <inheritdoc />
    public Task<DeclarationEntry?> GetAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(logicalName)) return Task.FromResult<DeclarationEntry?>(null);

        // 布局化命名（role/basename）不可逆：按逻辑名扫描命中（单次目录枚举，声明面规模有限）。
        if (_logicalNameOf is not null)
        {
            var normalized = UsharpSourceLoader.NormalizeLogicalName(logicalName);
            foreach (var path in Directory.EnumerateFiles(_root, "*" + UsharpSourceLoader.Extension, SearchOption.AllDirectories))
            {
                if (!string.Equals(LogicalNameOf(path), normalized, StringComparison.OrdinalIgnoreCase)) continue;
                return Task.FromResult<DeclarationEntry?>(
                    new DeclarationEntry(normalized, File.ReadAllText(path), null, path));
            }
            return Task.FromResult<DeclarationEntry?>(null);
        }

        var pathByRelative = ToPhysicalPath(logicalName);
        if (!File.Exists(pathByRelative)) return Task.FromResult<DeclarationEntry?>(null);

        var entry = new DeclarationEntry(UsharpSourceLoader.NormalizeLogicalName(logicalName), File.ReadAllText(pathByRelative), null, pathByRelative);
        return Task.FromResult<DeclarationEntry?>(entry);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(logicalName)) return Task.FromResult(false);
        if (_logicalNameOf is null) return Task.FromResult(File.Exists(ToPhysicalPath(logicalName)));

        var normalized = UsharpSourceLoader.NormalizeLogicalName(logicalName);
        foreach (var path in Directory.EnumerateFiles(_root, "*" + UsharpSourceLoader.Extension, SearchOption.AllDirectories))
        {
            if (string.Equals(LogicalNameOf(path), normalized, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task PutAsync(DeclarationEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(entry.LogicalName)) return Task.CompletedTask;

        // 已有声明：写回其真实物理位置（布局化命名下逻辑名不再等于相对路径）。
        if (!string.IsNullOrWhiteSpace(entry.SourcePath) && File.Exists(entry.SourcePath))
        {
            File.WriteAllText(entry.SourcePath!, entry.Text);
            return Task.CompletedTask;
        }

        if (_logicalNameOf is not null)
        {
            var normalized = UsharpSourceLoader.NormalizeLogicalName(entry.LogicalName);
            foreach (var existing in Directory.EnumerateFiles(_root, "*" + UsharpSourceLoader.Extension, SearchOption.AllDirectories))
            {
                if (!string.Equals(LogicalNameOf(existing), normalized, StringComparison.OrdinalIgnoreCase)) continue;
                File.WriteAllText(existing, entry.Text);
                return Task.CompletedTask;
            }
        }

        var path = ToPhysicalPath(entry.LogicalName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, entry.Text);
        return Task.CompletedTask;
    }

    /// <summary>物理路径 → 逻辑名（布局投影优先；缺省 = 根相对、<c>/</c> 归一、去扩展名）。</summary>
    private string LogicalNameOf(string physicalPath)
        => _logicalNameOf?.Invoke(physicalPath) ?? ToLogicalName(physicalPath);

    /// <summary>物理路径 → 逻辑名（根相对、<c>/</c> 归一、去扩展名）。</summary>
    private string ToLogicalName(string physicalPath)
    {
        var relative = Path.GetRelativePath(_root, physicalPath).Replace('\\', '/');
        return StripExtension(relative);
    }

    /// <summary>逻辑名 → 物理路径（补 <c>.ushar</c> 扩展名，<c>/</c> → 平台分隔符）。</summary>
    private string ToPhysicalPath(string logicalName)
    {
        var name = UsharpSourceLoader.NormalizeLogicalName(logicalName);
        if (!name.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase))
            name += UsharpSourceLoader.Extension;
        return Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string StripExtension(string relative)
        => relative.EndsWith(UsharpSourceLoader.Extension, StringComparison.OrdinalIgnoreCase)
            ? relative[..^UsharpSourceLoader.Extension.Length]
            : relative;
}
