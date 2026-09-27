using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 装配段基类：成功记账 / 失败记账 + 断链（取消与异常均重抛）。
    /// <para>
    /// 与 <c>CommandFramework.Pipeline.PipelineStageBase</c>（命令段：<c>catch(Exception)</c> 吞异常、管线继续）语义**相反**：
    /// 装配不变式要求失败即断链，故本基类内建两处 <c>throw</c>。两者并存期间不得互相继承（命名消歧）。
    /// </para>
    /// <para>通用件：仅依赖 <see cref="Modules.Pipeline"/>，零领域类型（框架零领域依赖不变量）。</para>
    /// </summary>
    /// <typeparam name="TContext">装配上下文（承载装配配置与装配产物）。</typeparam>
    public abstract class AssemblyStageBase<TContext> : IPipelineStage<TContext>
        where TContext : IPipelineContext
    {
        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <inheritdoc />
        public virtual bool CanExecute(TContext context) => true;

        /// <inheritdoc />
        public async Task ExecuteAsync(TContext context, CancellationToken ct = default)
        {
            try
            {
                await ExecuteCoreAsync(context, ct).ConfigureAwait(false);
                context.SetStageResult(Name, true);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消不属于装配失败：不记账，重抛交由管线/调用方按取消语义处理
            }
            catch (Exception ex)
            {
                context.SetStageResult(Name, false, ex.Message);
                throw; // 记账 + 断链（失败即断链是装配不变式）
            }
        }

        /// <summary>段内装配体（成功返回即记为成功；抛出即记账 + 断链）。</summary>
        protected abstract Task ExecuteCoreAsync(TContext context, CancellationToken ct);
    }
}
