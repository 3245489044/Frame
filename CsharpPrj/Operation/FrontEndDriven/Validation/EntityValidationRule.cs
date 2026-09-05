#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECS.Bridge;
using ECS.Core;
using ValidationFramework.Abstractions;

namespace FrontEndDriven.Validation;

/// <summary>
/// 实体校验目标提供者：经框架 <see cref="IValidationContext"/> 的提供者通道传给实体校验规则
/// （框架规则签名为 JsonElement + IValidationContext，实体校验所需的 EntityManager/实体集/控件池经此承载）。
/// </summary>
public sealed class EntityValidationTarget
{
    public EntityManager EntityManager { get; }

    /// <summary>null = 校验全部实体；非 null = 仅校验指定实体集（ValidateEntity 用）。</summary>
    public IReadOnlyList<Entity>? Entities { get; }

    public ControlPool? ControlPool { get; }

    public IReadOnlyDictionary<string, object?>? CustomData { get; }

    public EntityValidationTarget(
        EntityManager entityManager,
        IReadOnlyList<Entity>? entities = null,
        ControlPool? controlPool = null,
        IReadOnlyDictionary<string, object?>? customData = null)
    {
        EntityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        Entities = entities;
        ControlPool = controlPool;
        CustomData = customData;
    }
}

/// <summary>
/// <see cref="IEntityValidator"/> → <see cref="IValidationRule"/> 适配器：
/// 把单实体校验器包装为框架规则，在一次 ValidateAsync 中遍历目标实体集合并产出框架诊断。
/// 使 FrontEndDriven 的实体校验规则可注册进 ValidationFramework 的 ValidationEngine（类别分组/执行序/严格短路/报告统一）。
/// </summary>
internal sealed class EntityValidationRule : IValidationRule
{
    private readonly IEntityValidator _validator;

    public EntityValidationRule(IEntityValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public string RuleName => _validator.ValidatorName;

    public string Description => _validator.ValidatorName;

    public ValidationRuleCategory Category => MapCategory(_validator.Category);

    public Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = context.GetProvider<EntityValidationTarget>()
            ?? throw new InvalidOperationException("缺少 EntityValidationTarget 提供者（EntityValidationEngine 未装配）");
        var em = target.EntityManager;

        var feContext = new FrontEndDriven.Validation.ValidationContext { ControlPool = target.ControlPool };
        if (target.CustomData != null)
        {
            foreach (var kvp in target.CustomData)
                feContext.CustomData[kvp.Key] = kvp.Value;
        }

        var result = new ValidationResult();
        var entities = target.Entities ?? em.GetAllEntities().ToList();
        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _validator.Validate(entity, em, feContext, result);
        }

        var diagnostics = new List<ValidationDiagnostic>(result.Errors.Count + result.Warnings.Count);
        foreach (var error in result.Errors)
            diagnostics.Add(ToDiagnostic(error, Severity.Error));
        foreach (var warning in result.Warnings)
            diagnostics.Add(ToDiagnostic(warning, Severity.Warning));
        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private ValidationDiagnostic ToDiagnostic(ValidationError error, Severity severity)
    {
        var location = new SourceLocation(error.EntityName);
        var props = new Dictionary<string, object?>
        {
            ["entityId"] = error.EntityId.ToString(),
            ["entityName"] = error.EntityName,
            ["category"] = error.Category,
            ["componentType"] = error.ComponentType
        };
        return severity == Severity.Error
            ? ValidationDiagnostic.Error(RuleName, error.Message, location, null, props)
            : ValidationDiagnostic.Warning(RuleName, error.Message, location, null, props);
    }

    private static ValidationRuleCategory MapCategory(ValidationCategory category)
        => category switch
        {
            ValidationCategory.Completeness => ValidationRuleCategory.Structure,
            ValidationCategory.EventBinding => ValidationRuleCategory.Reference,
            ValidationCategory.Layout => ValidationRuleCategory.Semantic,
            ValidationCategory.Style => ValidationRuleCategory.Semantic,
            _ => ValidationRuleCategory.Semantic
        };
}
