#nullable enable
using System;
using System.Collections.Generic;
using ECS.Core;
using TConfig.ECS.Components;
using FrontEndDriven.Shared;

namespace FrontEndDriven.Validation.Validators;

public class EventBindingValidator : IEntityValidator
{
    public string ValidatorName => "EventBinding";

    public ValidationCategory Category => ValidationCategory.EventBinding;

    private static readonly HashSet<string> KnownEventNames = EventHelper.StandardEventTypes;

    public void Validate(Entity entity, EntityManager entityManager,
                         ValidationContext context, ValidationResult result)
    {
        if (!entityManager.TryGetComponent<EventBindingsComponent>(entity, out var bindings)
            || bindings.Bindings == null || bindings.Bindings.Count == 0)
            return;

        var entityName = EntityValidationHelper.GetEntityName(entity, entityManager);

        var seenBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < bindings.Bindings.Count; i++)
        {
            var binding = bindings.Bindings[i];

            if (string.IsNullOrWhiteSpace(binding.EventName))
            {
                result.AddError(new ValidationError(
                    entity, entityName, Category,
                    $"EventBindingsComponent.Bindings[{i}].EventName 为空",
                    ValidationSeverity.Error,
                    nameof(EventBindingsComponent)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.FunctionName))
            {
                result.AddError(new ValidationError(
                    entity, entityName, Category,
                    $"EventBindingsComponent.Bindings[{i}].FunctionName 为空",
                    ValidationSeverity.Error,
                    nameof(EventBindingsComponent)));
                continue;
            }

            if (!KnownEventNames.Contains(binding.EventName))
            {
                result.AddWarning(new ValidationError(
                    entity, entityName, Category,
                    $"EventBindingsComponent.Bindings[{i}].EventName = \"{binding.EventName}\" " +
                    "不是已知的 WinForms 事件名（可能为自定义事件）",
                    ValidationSeverity.Warning,
                    nameof(EventBindingsComponent)));
            }
            else if (!EventSubscriptionHelper.PhysicallySubscribableEvents.Contains(binding.EventName))
            {
                // E5·词表漂移可排查化：事件名通过标准词表校验、但物理订阅分派器未实现挂接 →
                // 绑定时 TrySubscribe 必返回 false、实体被判 Failed。此处提前告警，避免"校验通过但绑定必失败"难排查。
                result.AddWarning(new ValidationError(
                    entity, entityName, Category,
                    $"EventBindingsComponent.Bindings[{i}].EventName = \"{binding.EventName}\" " +
                    "在标准事件名词表中、但当前物理订阅分派器（EventSubscriptionHelper.TrySubscribe）未实现该事件的物理挂接，绑定将失败",
                    ValidationSeverity.Warning,
                    nameof(EventBindingsComponent)));
            }

            var bindingKey = $"{binding.EventName}:{binding.FunctionName}";
            if (!seenBindings.Add(bindingKey))
            {
                result.AddWarning(new ValidationError(
                    entity, entityName, Category,
                    $"EventBindingsComponent.Bindings[{i}]: 事件 \"{binding.EventName}\" + " +
                    $"函数 \"{binding.FunctionName}\" 存在重复绑定",
                    ValidationSeverity.Warning,
                    nameof(EventBindingsComponent)));
            }
        }
    }
}