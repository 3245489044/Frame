#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Share.Projection;

/// <summary>投影源（只读取源；缺源 ⇒ NotAttached，禁静默空）。</summary>
/// <typeparam name="TSource">源类型。</typeparam>
public interface IProjectionSource<TSource>
{
    /// <summary>按上下文取源（异步由实现决定；不缓存、不写）。</summary>
    Task<ProjectionResult<TSource>> ReadAsync(ProjectionContext context, CancellationToken cancellationToken);
}
