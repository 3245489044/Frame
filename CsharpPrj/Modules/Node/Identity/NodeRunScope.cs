namespace Modules.Node.Identity;

/// <summary>
/// <b>运行域</b>（Run Scope）：框架层对「当前 run 是谁」的唯一表达 —— 身份 = (域键, 容器, 轮次)。
/// <para>
/// 存在理由：框架只知「某域 + 某位置 + 某键」，运行态事实（过程/事件等内存面）的归属校验与身份补齐
/// 原先只能靠各调用方自觉传对 <c>container</c>；本记录把它升为可校验的一等概念。
/// 不改位置层语义：运行域**不是位置**（内存面不登记进 <c>PlacementRegistry</c> / <c>ICarrierRegistry</c>）。
/// </para>
/// <para>
/// 承载纪律：由宿主在**装配期注入**、按实例分区承载；<b>禁</b>以异步环境上下文（ambient 局部变量）或
/// 进程级单值承载身份（门禁 <c>Check-NodeFramework</c> B/E；多任务并发下进程级单值必然串味）。
/// </para>
/// </summary>
/// <param name="DomainKey">域键（如 <c>process</c>）。</param>
/// <param name="Container">容器（身份键首段；= 任务键 taskKey）。</param>
/// <param name="Round">轮次号。</param>
/// <param name="StartedAt">运行域建立时刻（UTC）；承载面无该事实 ⇒ <c>null</c>（不伪造）。</param>
public sealed record NodeRunScope(string DomainKey, string Container, int Round, DateTimeOffset? StartedAt = null);

/// <summary>
/// 运行域供体（宿主注入）：按「域键 + 容器」给出当前运行域；<b>无运行域 ⇒ null</b>（不伪造）。
/// <para>
/// 签名带 <paramref name="container"/> 而非仅域键：同一装配实例可能承载多个并发 run
/// （per-taskKey 分区），只凭域键无法判定「哪一个 run」——身份必须**显式给出**，
/// 不得由供体回读 ambient 运行实例（对齐 INV-I3「单一 ambient 通道只降不升」）。
/// </para>
/// </summary>
public interface INodeRunScopeSource
{
    /// <summary>取该（域键, 容器）对应的运行域；未建立 / 已释放 ⇒ <c>null</c>。</summary>
    /// <param name="domainKey">域键。</param>
    /// <param name="container">容器（= 任务键）。</param>
    NodeRunScope? Current(string domainKey, string container);
}

/// <summary>未装配供体：恒 <c>null</c>（不伪造运行域；未装配即"无身份校验"，行为与既有逐字一致）。</summary>
public sealed class NullNodeRunScopeSource : INodeRunScopeSource
{
    public static readonly NullNodeRunScopeSource Instance = new();

    private NullNodeRunScopeSource()
    {
    }

    /// <inheritdoc />
    public NodeRunScope? Current(string domainKey, string container) => null;
}
