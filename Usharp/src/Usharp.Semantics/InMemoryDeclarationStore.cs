using System.Collections.Generic;

namespace Usharp.Semantics;

/// <summary>
/// 声明库的内存实现（测试 / 无宿主设施场景）：逻辑名 → 文档的内存字典，进程重启即失。
/// <para>声明序保插入序（<see cref="ListAsync"/> 按插入序返回），保证装载复现；逻辑名按序数比较（<see cref="StringComparer.Ordinal"/>）。</para>
/// </summary>
public sealed class InMemoryDeclarationStore : IDeclarationStore
{
    private readonly Dictionary<string, DeclarationEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>由一组文档构造（重复逻辑名取后者并保留首个插入位序）。</summary>
    public InMemoryDeclarationStore(IEnumerable<DeclarationEntry>? entries = null)
    {
        if (entries is null) return;
        foreach (var entry in entries) Put(entry);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeclarationEntry>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<DeclarationEntry> snapshot = _order.Select(name => _entries[name]).ToList();
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<DeclarationEntry?> GetAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(logicalName) && _entries.TryGetValue(logicalName, out var entry))
            return Task.FromResult<DeclarationEntry?>(entry);

        return Task.FromResult<DeclarationEntry?>(null);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string logicalName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(logicalName) && _entries.ContainsKey(logicalName));
    }

    /// <inheritdoc />
    public Task PutAsync(DeclarationEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Put(entry);
        return Task.CompletedTask;
    }

    private void Put(DeclarationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.LogicalName)) return;

        if (!_entries.ContainsKey(entry.LogicalName)) _order.Add(entry.LogicalName);
        _entries[entry.LogicalName] = entry;
    }
}
