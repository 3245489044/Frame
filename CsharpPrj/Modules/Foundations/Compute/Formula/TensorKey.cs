namespace Modules.Compute.Formula;

/// <summary>
/// 张量（场的名字）：<b>唯一的结构标识</b>，不做坐标/形状别名。
/// <para>名字用稳定字符串（<c>height_cm</c>、<c>climate</c>、<c>biome_pick</c>、<c>cave_mask</c>…），
/// 便于签名与缓存键在跨进程/跨会话保持可读。</para>
/// </summary>
public readonly record struct TensorKey(string Name)
{
    public override string ToString() => Name;
}
