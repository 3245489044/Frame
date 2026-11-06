namespace Modules.Foundations.Storage;

/// <summary>
/// afterId 分页游标（通用；纯状态）—— 统一「页大小 + 当前游标 + 已拉页数 + 最大页数护栏」。
/// <para>
/// 用法：读取页 → <see cref="ShouldContinue"/> 判定是否继续 → <see cref="Advance"/> 推进游标。
/// 超过 <see cref="MaxPages"/> 即抛（上下文写入异常信息），避免底层未严格按游标过滤时静默死循环。
/// </para>
/// <para>本类型只推进状态，不执行查询、不持有存储连接、不含实体语义。</para>
/// </summary>
public sealed class PageCursor
{
    /// <summary>缺省最大页数护栏（与既有节点枚举口径一致）。</summary>
    public const int DefaultMaxPages = 10_000;

    /// <summary>缺省页大小。</summary>
    public const int DefaultPageSize = 200;

    /// <param name="context">游标上下文（写入越界异常信息，如 <c>nodeType=..., threadId=...</c>）。</param>
    /// <param name="pageSize">每页条数（&gt;0）。</param>
    /// <param name="maxPages">最大页数（&gt;0；缺省 <see cref="DefaultMaxPages"/>）。</param>
    public PageCursor(string? context = null, int pageSize = DefaultPageSize, int maxPages = DefaultMaxPages)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maxPages <= 0) throw new ArgumentOutOfRangeException(nameof(maxPages));
        Context = string.IsNullOrWhiteSpace(context) ? null : context;
        PageSize = pageSize;
        MaxPages = maxPages;
    }

    /// <summary>游标上下文（异常信息用；可空）。</summary>
    public string? Context { get; }

    /// <summary>每页条数。</summary>
    public int PageSize { get; }

    /// <summary>最大页数护栏。</summary>
    public int MaxPages { get; }

    /// <summary>当前游标（null = 首页）。</summary>
    public long? AfterId { get; private set; }

    /// <summary>已拉取的页数。</summary>
    public int Pages { get; private set; }

    /// <summary>本页是否已满（满页 ⇒ 可能还有下一页）。</summary>
    public bool ShouldContinue(int receivedCount) => receivedCount >= PageSize;

    /// <summary>
    /// 推进游标（<paramref name="lastPageId"/> = 本页最后一条的 Id）；超过最大页数 ⇒ 抛。
    /// </summary>
    /// <exception cref="InvalidOperationException">页数超过 <see cref="MaxPages"/>。</exception>
    public void Advance(long? lastPageId)
    {
        AfterId = lastPageId;
        Pages++;
        if (Pages >= MaxPages)
        {
            throw new InvalidOperationException(
                $"分页拉取超过 {MaxPages} 页上限（{Context ?? "未标注上下文"}），疑似 afterId 游标未严格推进。");
        }
    }
}
