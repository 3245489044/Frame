using System.Threading;

namespace CallCenter.Core.Router;

/// <summary>
/// 当前 <see cref="ToolRouter"/> 快照的线程安全持有器（P4-a Agent/FileDriven GUI 常驻宿主热换载体）。
/// <para>
/// <b>为什么需要它</b>：<see cref="ToolRouter.ReplaceTrack"/> 是「原位改共享字典」（<c>_activeByName/_activeOrder/_byId</c>），
/// 仅适合单线程/无并发读的调用方（如 Host CLI watch）。GUI 宿主是并发宿主——折叠算子侧
/// <see cref="Schema.ToolFunctionSchemaMapper"/> / 调用方会在后台热换同一 router 时并发读 <c>Tools</c>，
/// 若原位 ReplaceTrack 会与读取竞态（<c>ANTI-PARALLEL-RACE</c>）。
/// </para>
/// <para>
/// <b>语义</b>：热换 =「构建一个<b>全新</b> ToolRouter（含最新轨，如重发现后的 CSharpTrack）→
/// <see cref="Replace"/> 原子换当前引用」。读取方每次经 <see cref="Current"/> 拿到的都是<b>一个完整一致的路由快照</b>，
/// 没有「读到一半正在被改的字典」——天然消除原位改共享结构的竞态。旧 router 及其在途 delegate 由调用方持有实例继续完成，
/// 无在途后即被 GC（本类不持版本表；如需在途计数归零回收语义，配合 <see cref="Cache.HotReloadVersionStore"/>）。
/// </para>
/// </summary>
public sealed class ToolRouterReference
{
    private ToolRouter _current;

    /// <summary>以初始 router 构造（不可为 null；可传空 router 后经 <see cref="Replace"/> 首载）。</summary>
    public ToolRouterReference(ToolRouter initial)
    {
        _current = initial ?? throw new System.ArgumentNullException(nameof(initial));
    }

    /// <summary>当前激活的路由快照（读取始终拿到完整一致版本，无锁）。</summary>
    public ToolRouter Current => Volatile.Read(ref _current);

    /// <summary>原子替换当前引用为全新 router 快照。旧 router 在途调用由调用方持有实例完成。</summary>
    public void Replace(ToolRouter next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Volatile.Write(ref _current, next);
    }
}
