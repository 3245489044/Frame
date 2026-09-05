#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Validation;
using Modules.Pipeline;

namespace FrontEndDriven.Pipeline;

/// <summary>
/// 渲染校验阶段（审计）：渲染区间结束后执行对齐 ValidationFramework 的实体校验（完整度/布局/样式/事件绑定），
/// 经 ValidationService 组装 RenderAuditReport 并日志。
/// 纯审计：非抛出、不阻断渲染，使验证引擎成为渲染管线真实消费方。
/// </summary>
public sealed class RenderValidationStage : PipelineStageBase<RenderContext>
{
    private readonly EntityManager _entityManager;
    private readonly ValidationService _validationService;

    public RenderValidationStage(EntityManager entityManager, ValidationService? validationService = null)
        : base("RenderValidationStage", "EntityValidationEngine.ValidateAll（对齐 ValidationFramework 的实体审计）")
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _validationService = validationService
            ?? AppServiceProvider.Resolve<ValidationService>()
            ?? new ValidationService();
    }

    public override bool CanExecute(RenderContext context)
        => context.PartRender;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        _validationService.Validate(_entityManager, ctx.Scope);
        return Task.CompletedTask;
    }
}
