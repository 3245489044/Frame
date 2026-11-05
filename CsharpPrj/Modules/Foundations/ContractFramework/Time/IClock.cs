namespace ContractFramework.Time;

/// <summary>时钟端口（唯一时间源）：业务代码禁直读 <c>DateTime.Now</c>，时间由参数/端口注入。</summary>
public interface IClock
{
    /// <summary>当前 UTC 时刻（单调语义由实现决定；默认系统时钟）。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>系统时钟（缺省实现；仅此一处允许读真实时间）。</summary>
public sealed class SystemClock : IClock
{
    /// <summary>单例（无状态）。</summary>
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
