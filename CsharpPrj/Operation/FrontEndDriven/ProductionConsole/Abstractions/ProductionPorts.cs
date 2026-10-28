using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole;

/// <summary>能力清单端口：插件从声明面构建，控制台只读（不感知具体业务）。</summary>
public interface IProductionCatalog
{
    IReadOnlyList<CapabilityDescriptor> List();

    CapabilityDescriptor? Describe(string capabilityId);
}

/// <summary>
/// 任务运行端口：提交立即返回 jobId（入队即返回），进度/取消由实现内部状态机承担。
/// 插件实现负责把 jobId 映射到自身执行链；框架只消费快照。
/// </summary>
public interface IProductionRunner
{
    Task<string> SubmitAsync(
        string capabilityId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);

    Task<bool> CancelAsync(string jobId, CancellationToken ct = default);

    JobSnapshot? Get(string jobId);

    IReadOnlyList<JobSnapshot> Recent(int limit);

    /// <summary>任务状态变化（实现已节流；订阅方只做局部写回）。</summary>
    event Action<JobSnapshot>? Changed;
}

/// <summary>历史查询条件（分页；空值 = 不过滤）。</summary>
public sealed class ContainerQuery
{
    public string? CapabilityId { get; set; }

    public JobStatus? Status { get; set; }

    /// <summary>模糊匹配：jobId / inputHash / 参数文本。</summary>
    public string? Text { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>分页结果。</summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; init; } = new();

    public int Total { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int PageCount => PageSize <= 0 ? 0 : (Total + PageSize - 1) / PageSize;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) =>
        new() { Page = page, PageSize = pageSize };
}

/// <summary>历史存储端口（文件/数据库实现由运行时提供）。</summary>
public interface IProductionContainer
{
    void Append(JobSnapshot snapshot);

    PagedResult<JobSnapshot> Query(ContainerQuery query);

    JobSnapshot? LastByInputHash(string inputHash);
}

/// <summary>产物宿主（由 WebView2 虚拟主机适配器实现）：把绝对路径映射为可访问 URL；不支持返回 null。</summary>
public interface IProductionArtifactHost
{
    string? ResolveUrl(string absolutePath);
}

/// <summary>产物物化端口：data URI / 宿主 URL / 失败原因三态（渲染层只消费结果）。</summary>
public interface IArtifactPublisher
{
    Task<ArtifactUrl> MaterializeAsync(ArtifactSpec artifact, CancellationToken ct = default);
}
