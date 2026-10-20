using System.Collections.Concurrent;

namespace Share.ContextGeometry;

/// <summary>
/// 进程内内容寻址存储（有界；纯内存，零 IO）。
/// <para>幂等：同键同引用；超容清理最早写入项（有界，禁无界）。</para>
/// </summary>
public sealed class InMemorySharedBlockStore : ISharedBlockStore
{
    private readonly ConcurrentDictionary<string, string> _blocks = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private readonly int _capacity;

    /// <param name="capacity">容量上限（≤0 归一为 256）。</param>
    public InMemorySharedBlockStore(int capacity = 256)
        => _capacity = capacity <= 0 ? 256 : capacity;

    /// <summary>当前条目数（诊断用）。</summary>
    public int Count => _blocks.Count;

    /// <inheritdoc />
    public bool TryResolve(string contentKey, out string reference)
    {
        reference = string.Empty;
        if (string.IsNullOrWhiteSpace(contentKey)) return false;
        if (!_blocks.TryGetValue(contentKey, out var value)) return false;
        reference = value;
        return true;
    }

    /// <inheritdoc />
    public string Put(string contentKey, string canonicalBytes)
    {
        if (string.IsNullOrWhiteSpace(contentKey))
            throw new ArgumentException("内容键不得为空。", nameof(contentKey));

        if (_blocks.TryGetValue(contentKey, out var existing)) return existing;

        _blocks[contentKey] = canonicalBytes ?? string.Empty;
        _order.Enqueue(contentKey);
        Trim();
        return contentKey;
    }

    private void Trim()
    {
        while (_blocks.Count > _capacity && _order.TryDequeue(out var oldest))
        {
            if (string.IsNullOrWhiteSpace(oldest)) continue;
            _blocks.TryRemove(oldest, out _);
        }
    }
}
