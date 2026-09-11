namespace DataCenter.Abstractions.Lifecycle;

/// <summary>初始化编排结果。</summary>
public readonly record struct InitializationOutcome(bool Ok, string Message)
{
    public static InitializationOutcome Success(string message = "") => new(true, message);

    public static InitializationOutcome Failure(string message) => new(false, message);
}

/// <summary>
/// 状态初始化编排端口 —— 取代 Core 对宿主初始化块（Block）的直引。
/// 默认实现：宿主侧 <c>BlockStateInitializer</c>（DataCenter.Adapters ← BasicMethod.Block）。
/// </summary>
public interface IStateInitializer
{
    Task<InitializationOutcome> InitializeAsync(CancellationToken cancellationToken = default);
}
