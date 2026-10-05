namespace Modules.Node.Gather;

/// <summary>
/// 读侧**适配结果三态**（框架中性版）：与统一节点域端口的读三态同词、**不复用**其 Agent 类型
/// （框架层 2 禁引 <c>Agent.Contract</c>）。
/// <para>
/// 语义（F-4）：<c>Ok</c> 有行 / <c>OkButEmpty</c> 可用但空（**不得**据此回退或清面）/
/// <c>Unavailable</c> 不可用（唯一允许回退的态）。
/// </para>
/// </summary>
/// <param name="Status">状态（<c>Ok</c> / <c>OkButEmpty</c> / <c>Unavailable</c>）。</param>
/// <param name="Reasons">可见化原因（结构事实；禁叙述性理由）。</param>
public sealed record NodeGatherOutcome(string Status, IReadOnlyList<string> Reasons)
{
    /// <summary>读面可用且**有行**。</summary>
    public const string OkStatus = "Ok";

    /// <summary>读面可用但**无行**（不是不可用：不得据此回退/清除）。</summary>
    public const string OkButEmptyStatus = "OkButEmpty";

    /// <summary>读面**不可用**（唯一允许触发回退/清除的状态）。</summary>
    public const string UnavailableStatus = "Unavailable";

    /// <summary>读面可用（含空集）。</summary>
    public bool IsOk => Status is OkStatus or OkButEmptyStatus;

    /// <summary>读面不可用（唯一允许回退的态）。</summary>
    public bool IsUnavailable => string.Equals(Status, UnavailableStatus, StringComparison.Ordinal);

    /// <summary>读面可用但无行。</summary>
    public bool IsEmpty => string.Equals(Status, OkButEmptyStatus, StringComparison.Ordinal);

    /// <summary>按行数归 <c>Ok</c> / <c>OkButEmpty</c>。</summary>
    public static NodeGatherOutcome Available(int count, IReadOnlyList<string>? reasons = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new NodeGatherOutcome(
            count == 0 ? OkButEmptyStatus : OkStatus,
            reasons ?? Array.Empty<string>());
    }

    /// <summary>建「不可用」结果（恒空行语义 + 原因）。</summary>
    public static NodeGatherOutcome Unavailable(IReadOnlyList<string> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        return new NodeGatherOutcome(UnavailableStatus, reasons);
    }

    /// <summary>可用但空（域已装配、本容器无事实）。</summary>
    public static NodeGatherOutcome Empty(IReadOnlyList<string>? reasons = null) =>
        new(OkButEmptyStatus, reasons ?? Array.Empty<string>());
}
