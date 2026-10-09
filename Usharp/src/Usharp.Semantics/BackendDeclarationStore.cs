using System.Collections.Generic;

namespace Usharp.Semantics;

/// <summary>
/// 声明库的**键值后端端口**（数据库 / 配置行的最小抽象）：前缀域内 键 → 文本。
/// <para>
/// 本端口把「存哪」与「怎么当声明库用」分开：后端（<c>ur_usharp</c> 表 / <c>ur_config</c> / 节点库）
/// 由存储层实现，本层只约定三原语（列 / 读 / 写）。零外部依赖（纯字符串 + BCL Task）。
/// </para>
/// </summary>
public interface IDeclarationBackend
{
    /// <summary>列出以 <paramref name="prefix"/> 开头的全部（键, 文本）；键含前缀。</summary>
    Task<IReadOnlyList<(string Key, string Text)>> ListAsync(string prefix, CancellationToken ct = default);

    /// <summary>按键取文本；不存在返回 <c>null</c>（不抛）。</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>写入/覆盖键 → 文本。</summary>
    Task PutAsync(string key, string text, CancellationToken ct = default);
}

/// <summary>
/// 声明库的**后端实现**：以 <see cref="IDeclarationBackend"/> 为存储，把「前缀域 + 键」投影为逻辑名
/// （如 前缀 <c>usharp/</c> + 键 <c>usharp/workflows/task-provision</c> ⇒ 逻辑名 <c>workflows/task-provision</c>）。
/// <para>
/// 这是「USharp 编排入库」的落地形态：数据库里存的是键 → 声明文本，本类把它还原为与
/// <see cref="FileDeclarationStore"/> **同形**的 <see cref="DeclarationEntry"/> 流（同一逻辑名键域 ⇒ 装载面零差异）。
/// </para>
/// </summary>
public sealed class BackendDeclarationStore : IDeclarationStore
{
    private readonly IDeclarationBackend _backend;
    private readonly string _prefix;
    private readonly string? _lib;

    /// <param name="backend">键值后端（存储层实现）。</param>
    /// <param name="prefix">前缀域（如 <c>usharp/</c>；逻辑名 = 键去该前缀）。</param>
    /// <param name="lib">本库全部文档的 lib 归属（null = 缺省库）。</param>
    public BackendDeclarationStore(IDeclarationBackend backend, string prefix = "", string? lib = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _prefix = prefix ?? string.Empty;
        _lib = lib;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeclarationEntry>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _backend.ListAsync(_prefix, ct).ConfigureAwait(false);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Key))
            .Select(r => new DeclarationEntry(StripPrefix(r.Key), r.Text ?? string.Empty, _lib, r.Key))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<DeclarationEntry?> GetAsync(string logicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) return null;

        var key = _prefix + Normalize(logicalName);
        var text = await _backend.GetAsync(key, ct).ConfigureAwait(false);
        return text is null ? null : new DeclarationEntry(Normalize(logicalName), text, _lib, key);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string logicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) return false;
        var text = await _backend.GetAsync(_prefix + Normalize(logicalName), ct).ConfigureAwait(false);
        return text is not null;
    }

    /// <inheritdoc />
    public Task PutAsync(DeclarationEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.LogicalName)
            ? Task.CompletedTask
            : _backend.PutAsync(_prefix + Normalize(entry.LogicalName), entry.Text ?? string.Empty, ct);
    }

    private string StripPrefix(string key)
        => key.StartsWith(_prefix, StringComparison.Ordinal) ? key[_prefix.Length..] : key;

    private static string Normalize(string logicalName) => logicalName.Trim().Replace('\\', '/');
}
