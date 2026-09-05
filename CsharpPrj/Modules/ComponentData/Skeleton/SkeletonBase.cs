
using System;
using System.Threading.Tasks;

namespace Modules.ComponentData.Skeleton
{
    public abstract class SkeletonBase<TInput, TOutput> : ISkeleton<TInput, TOutput>
    {
        public string Name { get; }
        public string Description { get; }

        protected SkeletonBase(string name = null, string description = null)
        {
            Name = name ?? GetType().Name;
            Description = description;
        }

        public TOutput Execute(TInput input, SkeletonContext context = null)
        {
            var ownsContext = context == null;
            var ctx = context ?? new SkeletonContext();
            try
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return ExecuteCore(input, ctx);
            }
            finally
            {
                if (ownsContext) ctx.Dispose();
            }
        }

        public async Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null)
        {
            var ownsContext = context == null;
            var ctx = context ?? new SkeletonContext();
            try
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return await ExecuteCoreAsync(input, ctx).ConfigureAwait(false);
            }
            finally
            {
                if (ownsContext) ctx.Dispose();
            }
        }

        protected abstract TOutput ExecuteCore(TInput input, SkeletonContext context);
        protected abstract Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context);
    }

    public abstract class SyncSkeletonBase<TInput, TOutput> : SkeletonBase<TInput, TOutput>
    {
        protected SyncSkeletonBase(string name = null, string description = null) : base(name, description) { }

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context)
        {
            return Task.FromResult(ExecuteCore(input, context));
        }
    }

    public class DelegateSkeleton<TInput, TOutput> : SkeletonBase<TInput, TOutput>
    {
        private readonly Func<TInput, SkeletonContext, TOutput> _sync;
        private readonly Func<TInput, SkeletonContext, Task<TOutput>> _async;

        public DelegateSkeleton(string name,
            Func<TInput, TOutput> sync = null,
            Func<TInput, Task<TOutput>> async = null,
            string description = null)
            : this(name,
                sync == null ? null : (i, _) => sync(i),
                async == null ? null : (i, _) => async(i),
                description)
        { }

        public DelegateSkeleton(string name,
            Func<TInput, SkeletonContext, TOutput> syncWithCtx = null,
            Func<TInput, SkeletonContext, Task<TOutput>> asyncWithCtx = null,
            string description = null) : base(name, description)
        {
            _sync = syncWithCtx;
            _async = asyncWithCtx;
        }

        protected override TOutput ExecuteCore(TInput input, SkeletonContext ctx) =>
            _sync != null ? _sync(input, ctx) :
            _async != null ? throw new NotSupportedException("同步执行需要提供同步委托，当前只有异步委托。请提供同步委托或使用 ExecuteAsync。") :
            throw new NotSupportedException("没有提供任何执行委托。");

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext ctx) =>
            _async != null ? _async(input, ctx) :
            Task.FromResult(ExecuteCore(input, ctx));
    }
}
