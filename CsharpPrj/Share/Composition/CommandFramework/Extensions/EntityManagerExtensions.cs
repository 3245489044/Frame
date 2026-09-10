
#nullable enable
using ECS.Core;

namespace CommandFramework.Extensions;

public static class EntityManagerExtensions
{
    /// <summary>
    /// 转发到 <see cref="EntityManager.AddOrSetComponent{T}"/> 的原子实现（实例方法优先于扩展，自动生效）。
    /// 保留扩展作为兼容别名；原子语义（检查+写入同锁、幂等）由实例方法唯一权威。
    /// </summary>
    public static void AddOrSetComponent<T>(this EntityManager entityManager, Entity entity, T component)
        where T : struct, IComponent
        => entityManager.AddOrSetComponent(entity, component);
}