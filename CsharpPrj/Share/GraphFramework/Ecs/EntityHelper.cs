
#nullable enable

using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs
{
    public static class EntityHelper
    {
        public static Entity FindOrCreateEntityWithComponent<T>(EntityManager entityManager)
            where T : struct, IComponent
        {
            foreach (var entity in entityManager.GetAllEntities())
            {
                if (entityManager.HasComponent<T>(entity))
                {
                    return entity;
                }
            }

            var newEntity = entityManager.CreateEntity();
            entityManager.AddComponent(newEntity, default(T));
            return newEntity;
        }

        public static void SetOrAddComponent<T>(EntityManager entityManager, Entity entity, T component)
            where T : struct, IComponent
        {
            if (entityManager.HasComponent<T>(entity))
            {
                entityManager.SetComponent(entity, component);
            }
            else
            {
                entityManager.AddComponent(entity, component);
            }
        }

        public static List<Entity> FindEntitiesWithComponent<T>(EntityManager entityManager)
            where T : struct, IComponent
        {
            var result = new List<Entity>();
            foreach (var entity in entityManager.GetAllEntities())
            {
                if (entityManager.HasComponent<T>(entity))
                {
                    result.Add(entity);
                }
            }
            return result;
        }

        public static T? GetComponentOrDefault<T>(EntityManager entityManager, Entity entity)
            where T : struct, IComponent
        {
            if (entityManager.TryGetComponent<T>(entity, out var component))
            {
                return component;
            }
            return null;
        }

        public static void RemoveAllComponents<T>(EntityManager entityManager)
            where T : struct, IComponent
        {
            foreach (var entity in entityManager.GetAllEntities())
            {
                if (entityManager.HasComponent<T>(entity))
                {
                    entityManager.RemoveComponent<T>(entity);
                }
            }
        }
    }
}


