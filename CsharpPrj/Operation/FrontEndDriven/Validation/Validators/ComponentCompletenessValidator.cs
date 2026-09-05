#nullable enable
using ECS.Core;
using ECS.Components;
using TConfig.ECS.Components;
using FrontEndDriven.ECS.Components;

namespace FrontEndDriven.Validation.Validators;

public class ComponentCompletenessValidator : IEntityValidator
{
    public string ValidatorName => "ComponentCompleteness";

    public ValidationCategory Category => ValidationCategory.Completeness;

    public void Validate(Entity entity, EntityManager entityManager,
                         ValidationContext context, ValidationResult result)
    {
        if (!entityManager.TryGetComponent<ControlTypeComponent>(entity, out _))
        {
            result.AddError(new ValidationError(
                entity,
                EntityValidationHelper.GetEntityName(entity, entityManager),
                Category,
                "Entity 缺少必需的 ControlTypeComponent",
                ValidationSeverity.Error,
                nameof(ControlTypeComponent)));
            return;
        }

        bool isSplitterPanel = entityManager.HasComponent<SplitterPanelComponent>(entity);
        if (!isSplitterPanel && !entityManager.HasComponent<ParentEntityComponent>(entity))
        {
            result.AddWarning(new ValidationError(
                entity,
                EntityValidationHelper.GetEntityName(entity, entityManager),
                Category,
                "Entity 缺少 ParentEntityComponent（根容器 Entity 可忽略此警告）",
                ValidationSeverity.Warning,
                nameof(ParentEntityComponent)));
        }

        if (entityManager.HasComponent<ProportionalLayoutComponent>(entity) &&
            !entityManager.HasComponent<ComputedLayoutComponent>(entity))
        {
            result.AddWarning(new ValidationError(
                entity,
                EntityValidationHelper.GetEntityName(entity, entityManager),
                Category,
                "有 ProportionalLayoutComponent 但缺少 ComputedLayoutComponent（LayoutSystem 尚未执行）",
                ValidationSeverity.Warning,
                nameof(ComputedLayoutComponent)));
        }

        if (entityManager.HasComponent<StyleRefComponent>(entity) &&
            !entityManager.HasComponent<MergedStyleComponent>(entity))
        {
            result.AddWarning(new ValidationError(
                entity,
                EntityValidationHelper.GetEntityName(entity, entityManager),
                Category,
                "有 StyleRefComponent 但缺少 MergedStyleComponent（StyleSystem 尚未执行）",
                ValidationSeverity.Warning,
                nameof(MergedStyleComponent)));
        }
    }
}