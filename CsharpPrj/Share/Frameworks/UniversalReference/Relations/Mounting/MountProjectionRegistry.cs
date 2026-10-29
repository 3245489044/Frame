using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 读投影 SPI（UMF · R4）：投影差异只以「注册一行」出现，门面内禁 <c>switch (projectionId)</c>。
/// </summary>
public interface IMountProjection
{
    /// <summary>投影 id（与 <see cref="MountBinding.ProjectionId"/> 逐字一致）。</summary>
    string ProjectionId { get; }

    /// <summary>按作用域读集合（时序投影须保持域内序，禁自行排序）。</summary>
    Task<MountSet> ReadAsync(MountScope scope, CancellationToken ct = default);
}

/// <summary>委托投影（组合根注册用；不引入第二套实现类型）。</summary>
public sealed class DelegateMountProjection : IMountProjection
{
    private readonly Func<MountScope, CancellationToken, Task<MountSet>> _read;

    public DelegateMountProjection(string projectionId, Func<MountScope, CancellationToken, Task<MountSet>> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionId);
        ArgumentNullException.ThrowIfNull(read);
        ProjectionId = projectionId;
        _read = read;
    }

    public string ProjectionId { get; }

    public Task<MountSet> ReadAsync(MountScope scope, CancellationToken ct = default)
        => _read(scope, ct);
}

/// <summary>
/// 投影注册表（UMF · R4，唯一注册点）：ProjectionId → 投影实现。
/// <para>通用性：新增投影 = 实现 <see cref="IMountProjection"/> + 注册一行；UI/Context 只按 id 取集合。</para>
/// </summary>
public sealed class MountProjectionRegistry
{
    private readonly Dictionary<string, IMountProjection> _projections = new(StringComparer.Ordinal);

    /// <summary>注册投影（重复 ⇒ 抛：禁静默覆盖）。</summary>
    public void Register(IMountProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ProjectionId);
        if (!_projections.TryAdd(projection.ProjectionId, projection))
            throw new InvalidOperationException($"挂载投影重复注册：{projection.ProjectionId}（单一实现原则）");
    }

    /// <summary>取投影（未注册 ⇒ null，调用方须显式可见化）。</summary>
    public IMountProjection? TryGet(string projectionId)
        => !string.IsNullOrWhiteSpace(projectionId) && _projections.TryGetValue(projectionId, out var found)
            ? found
            : null;

    /// <summary>已注册投影数（结构计数，门禁判据）。</summary>
    public int Count => _projections.Count;
}
