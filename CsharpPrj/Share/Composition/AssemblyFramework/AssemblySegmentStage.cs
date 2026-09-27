using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 段薄壳 —— 把「一段装配」委托为委托（替代平铺的多个单职责 Stage）。
    /// <para>错误路径与单职责 Stage 完全一致：仍经 <see cref="AssemblyStageBase{TContext}.ExecuteAsync"/>
    /// 的 <c>SetStageResult(Name,…)</c>（失败断链），<see cref="OperationCanceledException"/> 照旧重抛。</para>
    /// <para>通用件：零领域类型（名称/描述/执行体/门控一律由调用方注入）。</para>
    /// </summary>
    public sealed class AssemblySegmentStage<TContext> : AssemblyStageBase<TContext>
        where TContext : IPipelineContext
    {
        private readonly string _name;
        private readonly string _description;
        private readonly Func<TContext, CancellationToken, Task> _execute;
        private readonly Func<TContext, bool>? _canExecute;

        public AssemblySegmentStage(
            string name,
            string description,
            Func<TContext, CancellationToken, Task> execute,
            Func<TContext, bool>? canExecute = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _description = description ?? throw new ArgumentNullException(nameof(description));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public override string Name => _name;

        /// <inheritdoc />
        public override string Description => _description;

        /// <inheritdoc />
        public override bool CanExecute(TContext context) => _canExecute?.Invoke(context) ?? true;

        /// <inheritdoc />
        protected override Task ExecuteCoreAsync(TContext context, CancellationToken ct)
            => _execute(context, ct);
    }
}
