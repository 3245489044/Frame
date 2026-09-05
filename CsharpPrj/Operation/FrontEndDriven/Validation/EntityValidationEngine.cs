#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;
using FrontEndDriven.Shared.Utils;
using ValidationFramework.Abstractions;
using VfContext = ValidationFramework.Abstractions.ValidationContext;

namespace FrontEndDriven.Validation;

/// <summary>
/// 实体校验引擎（对齐 ValidationFramework）：把 <see cref="IEntityValidator"/> 注册为框架规则
/// <see cref="EntityValidationRule"/>，委托 ValidationFramework.ValidationEngine 执行
/// （类别分组/执行序/严格短路/报告统一均由框架承担），再把框架 <see cref="ValidationReport"/>
/// 投影回 FrontEndDriven 的 <see cref="ValidationResult"/>（保留 ECS 域视图，供审计/诊断消费）。
/// </summary>
public class EntityValidationEngine
{
    private readonly List<IEntityValidator> _validators = new();
    private readonly string _name;

    public EntityValidationEngine(string name = "EntityValidationEngine")
    {
        _name = name;
    }

    public IReadOnlyList<IEntityValidator> Validators => _validators.AsReadOnly();

    public void RegisterValidator(IEntityValidator validator)
    {
        if (validator == null)
            throw new ArgumentNullException(nameof(validator));

        _validators.Add(validator);
    }

    public void RegisterValidators(IEnumerable<IEntityValidator> validators)
    {
        if (validators == null)
            throw new ArgumentNullException(nameof(validators));

        foreach (var validator in validators)
        {
            RegisterValidator(validator);
        }
    }

    public bool UnregisterValidator(string validatorName)
    {
        var index = _validators.FindIndex(v => v.ValidatorName == validatorName);
        if (index >= 0)
        {
            _validators.RemoveAt(index);
            return true;
        }
        return false;
    }

    public ValidationResult ValidateAll(EntityManager entityManager, ValidationContext? context = null)
        => Run(entityManager, null, context, null);

    public ValidationResult ValidateByCategory(EntityManager entityManager,
        ValidationCategory category, ValidationContext? context = null)
        => Run(entityManager, null, context, category);

    public ValidationResult ValidateEntity(Entity entity, EntityManager entityManager,
        ValidationContext? context = null)
        => Run(entityManager, new[] { entity }, context, null);

    /// <summary>构造框架 ValidationEngine（适配器规则），执行并把 ValidationReport 投影回 ValidationResult。</summary>
    private ValidationResult Run(
        EntityManager entityManager,
        IReadOnlyList<Entity>? entities,
        ValidationContext? context,
        ValidationCategory? onlyCategory)
    {
        if (entityManager == null)
            throw new ArgumentNullException(nameof(entityManager));

        var engine = new ValidationFramework.Engine.ValidationEngine(_name);
        engine.RegisterRules(_validators
            .Where(v => !onlyCategory.HasValue || v.Category == onlyCategory.Value)
            .Select(v => (IValidationRule)new EntityValidationRule(v)));

        var target = new EntityValidationTarget(
            entityManager,
            entities,
            context?.ControlPool,
            context?.CustomData);
        var fcontext = new VfContext().WithProvider(target);

        var report = engine.ValidateAsync(default, fcontext).GetAwaiter().GetResult();
        int totalChecked = entities?.Count ?? entityManager.GetAllEntities().Count();
        return FromReport(report, totalChecked);
    }

    private static ValidationResult FromReport(ValidationReport report, int totalChecked)
    {
        var result = new ValidationResult { TotalChecked = totalChecked };
        foreach (var diagnostic in report.Diagnostics)
        {
            if (diagnostic.Severity == Severity.Error)
                result.AddError(ToValidationError(diagnostic, ValidationSeverity.Error));
            else if (diagnostic.Severity == Severity.Warning)
                result.AddWarning(ToValidationError(diagnostic, ValidationSeverity.Warning));
        }
        return result;
    }

    private static ValidationError ToValidationError(ValidationDiagnostic diagnostic, ValidationSeverity severity)
    {
        var props = diagnostic.Properties;
        var entityId = props != null && props.TryGetValue("entityId", out var rawId)
            && EntityIdParser.TryParse(rawId?.ToString() ?? string.Empty, out var parsed)
                ? parsed
                : Entity.Invalid;
        var entityName = props != null && props.TryGetValue("entityName", out var rawName)
            ? rawName?.ToString()
            : diagnostic.SourceLocation?.Source ?? "(unknown)";
        var category = props != null && props.TryGetValue("category", out var rawCat)
            && rawCat is ValidationCategory typedCat
                ? typedCat
                : ValidationCategory.Custom;
        var componentType = props != null && props.TryGetValue("componentType", out var rawComp)
            ? rawComp?.ToString()
            : null;

        return new ValidationError(entityId, entityName ?? "(unknown)", category, diagnostic.Message, severity, componentType);
    }
}