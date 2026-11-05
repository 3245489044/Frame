using Modules.Node.Carriers;
using Modules.Node.Diagnostics;

namespace Modules.Node.Instances;

/// <summary>
/// 承载逻辑容器（G-2 / §4.1）—— 框架只声明**逻辑契约**，物理建表属实现（守住零依赖）。
/// </summary>
public enum InstanceStoreContainer
{
    /// <summary>元信息（instanceId / kind / schemaVersion / parentId / createdAt / sealedAt）。</summary>
    Meta,

    /// <summary>状态 KV（实例切片的状态真源）。</summary>
    State,

    /// <summary>位置行（PlacementId ←→ 位置键；同实例的多个位置视图）。</summary>
    Placements,

    /// <summary>实例内节点行（NodeType 判别）。</summary>
    Nodes,

    /// <summary>边（owns / link / ref；实例内 + 跨实例引用）。</summary>
    Edges,

    /// <summary>追加日志（journal；兼作 Outbox 与观测源，只增不丢）。</summary>
    Events,

    /// <summary>快照 / 断点。</summary>
    Checkpoints,
}

/// <summary>承载内一条记录（键 + JSON 载荷 + 内容摘要 + 写入时刻；形态与载体一致，N11 JSON 单源）。</summary>
public sealed record InstanceStoreRecord(
    InstanceStoreContainer Container,
    string Key,
    string Json,
    string? Digest,
    DateTimeOffset WrittenAt);

/// <summary>
/// 单文件承载端口（G-2）—— 实例的**物理底座**：句柄 / 事务 / 封存 / 归档 + 身份↔定位反解。
/// <para>
/// 纪律：① 事务边界 = Store（单文件，I6）；② 一实例一 Store 句柄（I2）；③ 封存后只读（I3）；
/// ④ 实现一律在框架之外（SQLite/文件实现在上层，K6/§3.5）；⑤ 未装配 ⇒ <see cref="NullInstanceStore"/> + 可见化（N9）。
/// </para>
/// <para>身份可寻址（I1）：<see cref="Locate"/> / <see cref="TryResolveInstanceId"/> 双向可反解。</para>
/// </summary>
public interface IInstanceStore : IAsyncDisposable
{
    /// <summary>承载键（= <see cref="InstanceKindSpec.StoreKind"/>；实现按此键由宿主注入）。</summary>
    string StoreKey { get; }

    /// <summary>承载定位（单文件路径或等价定位串）—— 身份→承载的反解锚点（I1）。</summary>
    string Path { get; }

    /// <summary>当前承载阶段（I3 判据来源）。</summary>
    InstanceStage Stage { get; }

    /// <summary>身份 → 承载定位（纯函数，可在打开前调用：装配前即可确定写往何处）。</summary>
    string Locate(string instanceId);

    /// <summary>承载定位 → 身份（反解失败 ⇒ false，不抛：外来定位属可见化缺口）。</summary>
    bool TryResolveInstanceId(string location, out string instanceId);

    /// <summary>打开承载（句柄/租约就位；同实例重复打开幂等，异实例 fail-fast）。</summary>
    Task OpenAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>封存（只读；I3：此后写调用被拒并可见化）。</summary>
    Task SealAsync(CancellationToken cancellationToken = default);

    /// <summary>归档（只读；承载可迁出，身份仍可反解）。</summary>
    Task ArchiveAsync(CancellationToken cancellationToken = default);

    /// <summary>开启事务边界（单文件内多容器提交原子，I6）。</summary>
    Task<IInstanceUnitOfWork> BeginUnitOfWorkAsync(CancellationToken cancellationToken = default);

    /// <summary>读一条记录（缺省 ⇒ null；不抛）。</summary>
    Task<InstanceStoreRecord?> ReadAsync(InstanceStoreContainer container, string key, CancellationToken cancellationToken = default);

    /// <summary>写入一条记录（封存后 ⇒ Rejected；失败可见化，不静默丢）。</summary>
    Task<CarrierWriteResult> UpsertAsync(InstanceStoreContainer container, string key, string json, CancellationToken cancellationToken = default);

    /// <summary>按键前缀枚举（序 = 键序数序，N6）。</summary>
    Task<IReadOnlyList<string>> EnumerateAsync(InstanceStoreContainer container, string? keyPrefix = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 承载记录内容摘要（**单源**）—— 实现侧禁自写第二套摘要（对齐 N4 幂等 / N5 冲突判据）。
/// </summary>
public static class InstanceDigest
{
    /// <summary>内容摘要（SHA256 大写 hex）。</summary>
    public static string Of(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return NodeJson.Sha256Hex(json);
    }
}

/// <summary>事务边界（G-2）：单文件内多容器提交原子（粒度 A 天然满足；跨文件走声明协议，I6）。</summary>
public interface IInstanceUnitOfWork : IAsyncDisposable
{
    /// <summary>提交（此后写入对外可见）。</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>回滚（未提交的写入全部丢弃）。</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 未装配承载（G-2）：一切读写即 ∅ + 诊断，不静默兜底（N9）。
/// 出现该实现 = 宿主未按 <see cref="InstanceKindSpec.StoreKind"/> 注入承载实现。
/// </summary>
public sealed class NullInstanceStore : IInstanceStore
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullInstanceStore(string storeKey, INodeDiagnosticsSink? diagnostics = null)
    {
        StoreKey = storeKey;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public string StoreKey { get; }

    public string Path => $"null://{StoreKey}";

    public InstanceStage Stage => InstanceStage.Created;

    public string Locate(string instanceId)
    {
        Report("locate", instanceId);
        return Path;
    }

    public bool TryResolveInstanceId(string location, out string instanceId)
    {
        Report("resolve-id", location);
        instanceId = string.Empty;
        return false;
    }

    public Task OpenAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        Report("open", instanceId);
        return Task.CompletedTask;
    }

    public Task SealAsync(CancellationToken cancellationToken = default)
    {
        Report("seal", StoreKey);
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        Report("archive", StoreKey);
        return Task.CompletedTask;
    }

    public Task<IInstanceUnitOfWork> BeginUnitOfWorkAsync(CancellationToken cancellationToken = default)
    {
        Report("uow", StoreKey);
        return Task.FromResult<IInstanceUnitOfWork>(new NullUnitOfWork());
    }

    public Task<InstanceStoreRecord?> ReadAsync(
        InstanceStoreContainer container, string key, CancellationToken cancellationToken = default)
    {
        Report("read", $"{container}|{key}");
        return Task.FromResult<InstanceStoreRecord?>(null);
    }

    public Task<CarrierWriteResult> UpsertAsync(
        InstanceStoreContainer container, string key, string json, CancellationToken cancellationToken = default)
    {
        Report("upsert", $"{container}|{key}");
        return Task.FromResult(CarrierWriteResult.Rejected("instance-store-unbound"));
    }

    public Task<IReadOnlyList<string>> EnumerateAsync(
        InstanceStoreContainer container, string? keyPrefix = null, CancellationToken cancellationToken = default)
    {
        Report("enumerate", container.ToString());
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Report(string operation, string subject) =>
        _diagnostics.Report(new NodeDiagnostic(
            "instance.store-unbound",
            NodeDiagnosticSeverity.Warning,
            $"实例承载未装配，操作 '{operation}' 已跳过（不兜底）",
            $"{StoreKey}|{subject}"));

    private sealed class NullUnitOfWork : IInstanceUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

