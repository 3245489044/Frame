
#nullable enable

using FrontEndDriven.Shared.Framework;
using TConfig.ECS;

namespace FrontEndDriven.Shared.Utils;

public sealed class EntityIdCache
{
    /// <summary>
    /// 进程内共享单例：渲染管线中 WinFormsUIAdapter / WinFormApplier 等各消费点共用同一缓存。
    /// 消除「装配时各自 new 导致实例分叉」的脆弱性（D3）——Entity 为不可变值，共享缓存无副作用。
    /// </summary>
    public static EntityIdCache Instance { get; } = new();

    private readonly ParseCache<string, Entity> _cache;

    public EntityIdCache()
    {
        _cache = new ParseCache<string, Entity>(
            parser: EntityIdParser.TryParse,
            isValid: e => e.IsValid,
            comparer: StringComparer.Ordinal);
    }

    /// <summary>
    /// 仅缓存解析成功的结果；解析失败不写入缓存（负结果随时可重试）。
    /// 避免把「字符串格式非法」与「实体 ID 已销毁」混入同一桶导致长期返回 false 而无失效策略。
    /// 命中缓存时经 ParseCache 有效性谓词复核实体是否仍有效（已销毁 → 按未命中重新解析）。
    /// </summary>
    public bool TryParse(string str, out Entity entity)
        => _cache.TryParse(str, out entity);

    public void Clear() => _cache.Clear();
}