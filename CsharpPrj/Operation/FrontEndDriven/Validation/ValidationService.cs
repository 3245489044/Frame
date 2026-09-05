#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Validation.Diagnostics;
using FrontEndDriven.Validation.Validators;

namespace FrontEndDriven.Validation;

/// <summary>
/// 渲染校验服务：持有对齐 ValidationFramework 的 <see cref="EntityValidationEngine"/> + 默认实体校验器，
/// 提供「校验并组装 RenderAuditReport」的审计入口，供渲染管线 RenderValidationStage 消费
/// （audit-only，非抛出，不阻断渲染）。使此前未接线的验证子系统成为渲染管线的真实消费方。
/// </summary>
public sealed class ValidationService
{
    public EntityValidationEngine Engine { get; }

    public ValidationService(IEnumerable<IEntityValidator>? validators = null)
    {
        Engine = new EntityValidationEngine("FrontEndDriven.RenderValidation");
        Engine.RegisterValidators(validators ?? DefaultValidators());
    }

    /// <summary>默认实体校验器集（完整度 / 布局 / 样式 / 事件绑定）。</summary>
    public static IEnumerable<IEntityValidator> DefaultValidators()
    {
        yield return new ComponentCompletenessValidator();
        yield return new LayoutValidator();
        yield return new StyleValidator();
        yield return new EventBindingValidator();
    }

    /// <summary>校验渲染世界全部实体，组装审计报告并日志；任何校验器异常在此隔离为日志，不抛出。</summary>
    public RenderAuditReport Validate(EntityManager entityManager, string scope)
    {
        ArgumentNullException.ThrowIfNull(entityManager);

        var result = Engine.ValidateAll(entityManager);

        var report = new RenderAuditReport
        {
            Timestamp = DateTime.UtcNow,
            PipelineName = "RenderValidation",
            TotalEntities = entityManager.GetAllEntities().Count()
        };
        ValidationAuditCollector.Collect(report, result);

        FrontEndDrivenLog.Log("RenderValidationStage_summary",
            new { scope, summary = result.GetSummary(), issues = result.TotalIssues });

        if (result.TotalIssues > 0)
        {
            FrontEndDrivenLog.Log("RenderValidationStage_details",
                new { scope, detail = result.GetDetailText() });
        }

        return report;
    }
}
