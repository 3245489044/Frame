using System.Collections.Generic;
using System.Threading.Tasks;

namespace Share.Composition;

/// <summary>
/// 槽位请求 —— 源端口按槽位惰性取件的最小入参（槽标识 + 当前上下文）。
/// </summary>
/// <param name="Slot">槽位标识。</param>
/// <param name="Context">当前装配上下文。</param>
public sealed record SlotRequest(string Slot, IAssemblyContext Context);

/// <summary>
/// 源端口 —— 为槽位提供 / 解析部件（泛化既有装配数据源）。
/// 支持惰性 / 缓存 / 生成三种取件形态：缓存命中即返回、未命中即生成，均由实现方收敛（含校验与回退）。
/// 内核只经本端口消费数据，不反向依赖任何消费方类型。
/// </summary>
/// <typeparam name="TCtx">领域装配上下文（须实现 <see cref="IAssemblyContext"/>）。</typeparam>
public interface IAssemblySource<in TCtx> where TCtx : IAssemblyContext
{
    /// <summary>解析指定槽位的部件集合（无内容返回空集合，不返回 null）。</summary>
    Task<IReadOnlyList<IComponent>> ResolveAsync(TCtx ctx, SlotRequest request);
}
