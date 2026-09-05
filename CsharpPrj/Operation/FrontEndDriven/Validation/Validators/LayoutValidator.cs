#nullable enable
using ECS.Core;
using TConfig.ECS.Components;

namespace FrontEndDriven.Validation.Validators;

public class LayoutValidator : IEntityValidator
{
    public string ValidatorName => "Layout";

    public ValidationCategory Category => ValidationCategory.Layout;

    public void Validate(Entity entity, EntityManager entityManager,
                         ValidationContext context, ValidationResult result)
    {
        var entityName = EntityValidationHelper.GetEntityName(entity, entityManager);

        if (entityManager.TryGetComponent<ProportionalLayoutComponent>(entity, out var prop))
        {
            CheckProportionalValue(entity, entityName, "Left", prop.Left, result, Category);
            CheckProportionalValue(entity, entityName, "Top", prop.Top, result, Category);
            CheckProportionalValue(entity, entityName, "Width", prop.Width, result, Category);
            CheckProportionalValue(entity, entityName, "Height", prop.Height, result, Category);
        }

        if (entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var computed))
        {
            CheckNonNegative(entity, entityName, "Left", computed.Left, result, Category);
            CheckNonNegative(entity, entityName, "Top", computed.Top, result, Category);
            CheckNonNegative(entity, entityName, "Width", computed.Width, result, Category);
            CheckNonNegative(entity, entityName, "Height", computed.Height, result, Category);

            if (computed.ParentWidth > 0 && computed.ParentHeight > 0)
            {
                if (computed.Left + computed.Width > computed.ParentWidth)
                {
                    result.AddWarning(new ValidationError(
                        entity, entityName, Category,
                        $"ComputedLayoutComponent: Left({computed.Left}) + Width({computed.Width}) > " +
                        $"ParentWidth({computed.ParentWidth})，控件超出父容器右边界",
                        ValidationSeverity.Warning,
                        nameof(ComputedLayoutComponent)));
                }

                if (computed.Top + computed.Height > computed.ParentHeight)
                {
                    result.AddWarning(new ValidationError(
                        entity, entityName, Category,
                        $"ComputedLayoutComponent: Top({computed.Top}) + Height({computed.Height}) > " +
                        $"ParentHeight({computed.ParentHeight})，控件超出父容器下边界",
                        ValidationSeverity.Warning,
                        nameof(ComputedLayoutComponent)));
                }
            }
        }

        if (entityManager.TryGetComponent<SplitterRatioComponent>(entity, out var ratio))
        {
            if (ratio.Ratio < 0.0 || ratio.Ratio > 1.0)
            {
                result.AddError(new ValidationError(
                    entity, entityName, Category,
                    $"SplitterRatioComponent.Ratio = {ratio.Ratio:F4}，超出 0.0~1.0 范围",
                    ValidationSeverity.Error,
                    nameof(SplitterRatioComponent)));
            }
        }
    }

    private static void CheckProportionalValue(
        Entity entity, string entityName, string fieldName, float value,
        ValidationResult result, ValidationCategory category)
    {
        if (value < 0.0f || value > 1.0f)
        {
            result.AddError(new ValidationError(
                entity, entityName, category,
                $"ProportionalLayoutComponent.{fieldName} = {value:F4}，超出 0.0~1.0 范围",
                ValidationSeverity.Error,
                nameof(ProportionalLayoutComponent)));
        }
    }

    private static void CheckNonNegative(
        Entity entity, string entityName, string fieldName, int value,
        ValidationResult result, ValidationCategory category)
    {
        if (value < 0)
        {
            result.AddError(new ValidationError(
                entity, entityName, category,
                $"ComputedLayoutComponent.{fieldName} = {value}，不能为负数",
                ValidationSeverity.Error,
                nameof(ComputedLayoutComponent)));
        }
    }
}