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
    /// <remarks>
    /// 返回值恒为"规范字节"（命中与未命中返回同一类键：内容本身），
    /// 原实现未命中返回 contentKey、命中返回规范字节，导致 PrefixAssemblyPlan.CanonicalRef 在前缀缓存键中不可比
    /// （ContextGeometry.md §4.8，原 :33-44）。登记用 TryAdd 原子完成，消除 check-then-act 竞态（§4.9，原 :38-44）。
    /// </remarks>
    public string Put(string contentKey, string canonicalBytes)
    {
        if (string.IsNullOrWhiteSpace(contentKey))
            throw new ArgumentException("内容键不得为空。", nameof(contentKey));

        var value = canonicalBytes ?? string.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_blocks.TryAdd(contentKey, value))
            {
                _order.Enqueue(contentKey);
                Trim();
                return value;
            }

            // 同键已存在 ⇒ 同键同引用（首次写入的规范字节为准）。
            if (_blocks.TryGetValue(contentKey, out var existing))
                return existing;
        }

        // 并发 Trim 清掉后仍未登记成功：仍返回本次规范字节（调用方比较的是内容，不是驻留状态）。
        return value;
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
