using Modules.Compute.Formula;

namespace Share.ContextGeometry;

/// <summary>
/// 进程内内容寻址存储（有界；纯内存，零 IO）。
/// <para>幂等：同键同引用；超容清理最早写入项（有界，禁无界）。</para>
/// <para>有界/淘汰语义单源 = <see cref="BoundedCache{TKey,TValue}"/>（Modules.Foundations.Compute 基元）；
/// 本类只做内容键 → 规范字节的映射，禁再实现第二套淘汰算法（CG-02）。</para>
/// </summary>
public sealed class InMemorySharedBlockStore : ISharedBlockStore
{
    private readonly BoundedCache<string, string> _blocks;

    /// <param name="capacity">容量上限（≤0 归一为 256）。</param>
    public InMemorySharedBlockStore(int capacity = 256)
        => _blocks = new BoundedCache<string, string>(capacity);

    /// <summary>当前条目数（诊断用）。</summary>
    public int Count => _blocks.Count;

    /// <inheritdoc />
    public bool TryResolve(string contentKey, out string reference)
    {
        reference = string.Empty;
        if (string.IsNullOrWhiteSpace(contentKey)) return false;
        if (!_blocks.TryGet(contentKey, out var value) || value is null) return false;
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

        // 同键首写为准（同键同引用）；淘汰与容量由 BoundedCache 单点负责。
        return _blocks.GetOrAdd(contentKey, canonicalBytes ?? string.Empty);
    }
}
