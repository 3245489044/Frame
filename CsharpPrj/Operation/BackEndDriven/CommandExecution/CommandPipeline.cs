using ECS.Core;
using Modules.Pipeline.StaticPipeline;
using BackEndDriven.CommandExecution.Context;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// 命令执行管线工厂：将 CommandSystemCoordinator.Update() 的手写 for 循环
/// （逐个 pending 实体 ProcessEntity）收敛为 StaticPipeline，TContext = CommandPipelineContext
/// （唯一命令上下文：继承 CommandContext，承载命令状态 + IPipelineContext/TSPool 转发）。
/// 每条 pending 命令生成一个独立 Step，使 halt 经 Step.CanExecute 在命令边界生效（真短路）。
///
/// 语义映射：
/// - halt/forcehalt → ctx.StopRequested=true → 后续命令 Step 经 CanExecute 短路跳过
/// - 单命令失败 → 保持原语义（失败标记实体状态后继续后续命令，由 ProcessEntity 内部保证；IsFatal=false）
/// - 取消 → 经 ctx 传播的取消请求承载（原 getCancellation 死参数恒返回
///   CancellationToken.None，短路条件恒真、每次 Step 多余委托调用，已移除；P2-C1）
/// </summary>
public static class CommandPipelineFactory
{
    public static StaticPipeline<CommandPipelineContext> Create(
        IEnumerable<Entity> entities,
        Action<Entity, CommandPipelineContext> processEntity)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(processEntity);

        // 每条 pending 命令一个独立 Step：CanExecute 在命令边界检查
        // StopRequested（halt 短路），IsFatal=false 保持
        // "单命令失败标记实体后继续后续" 的原语义。
        var steps = new List<Step<CommandPipelineContext>>();
        var index = 0;
        foreach (var entity in entities)
        {
            var captured = entity;
            var stepIndex = index++;
            steps.Add(new Step<CommandPipelineContext>(
                $"Command[{stepIndex}]",
                ctx =>
                {
                    ctx.CurrentIndex = stepIndex;
                    processEntity(captured, ctx);
                },
                IsFatal: false,
                CanExecute: ctx => !ctx.StopRequested
                    && !ctx.IsCancelled));
        }

        return new StaticPipeline<CommandPipelineContext>(steps);
    }
}
