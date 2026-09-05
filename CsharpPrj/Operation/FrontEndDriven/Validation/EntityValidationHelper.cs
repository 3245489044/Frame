
#nullable enable
using ECS.Core;
using TConfig.ECS.Components;

namespace FrontEndDriven.Validation;

public static class EntityValidationHelper
{
    public static string GetEntityName(Entity entity, EntityManager entityManager)
    {
        if (entityManager.TryGetComponent<ControlTypeComponent>(entity, out var ctrlType))
        {
            return ctrlType.Name ?? $"Entity#{entity.Id}";
        }
        return $"Entity#{entity.Id}";
    }
}