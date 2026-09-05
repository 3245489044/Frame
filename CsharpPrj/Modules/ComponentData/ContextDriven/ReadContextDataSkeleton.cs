using ECS.Core;
using Modules.ComponentData.Skeleton;

namespace Modules.ComponentData.ContextDriven
{
    public class ReadContextDataSkeleton<T> : SkeletonBase<Unit, T>, IEcsSkeleton<Unit, T> where T : class
    {
        private readonly string? _key;

        public ReadContextDataSkeleton(string? key = null, string? name = null, string? desc = null)
            : base(name ?? $"ReadData<{typeof(T).Name}>", desc ?? $"Reads {typeof(T).Name} from context")
        { _key = key; }

        protected override T ExecuteCore(Unit input, SkeletonContext ctx)
        {
            var data = ctx.GetData<T>(_key);
            if (data == null)
                throw new InvalidOperationException($"Data of type {typeof(T).Name} not found{(string.IsNullOrEmpty(_key) ? "" : $" with key '{_key}'")} in context.");
            return data;
        }

        protected override Task<T> ExecuteCoreAsync(Unit input, SkeletonContext ctx) =>
            Task.FromResult(ExecuteCore(input, ctx));
    }

    public class WriteContextDataSkeleton<T> : SkeletonBase<T, Unit>, IEcsSkeleton<T, Unit> where T : class
    {
        private readonly string? _key;

        public WriteContextDataSkeleton(string? key = null, string? name = null, string? desc = null)
            : base(name ?? $"WriteData<{typeof(T).Name}>", desc ?? $"Writes {typeof(T).Name} to context")
        { _key = key; }

        protected override Unit ExecuteCore(T input, SkeletonContext ctx)
        {
            ctx.SetData(input, _key);
            return Unit.Default;
        }

        protected override Task<Unit> ExecuteCoreAsync(T input, SkeletonContext ctx)
        {
            ExecuteCore(input, ctx);
            return Task.FromResult(Unit.Default);
        }
    }

    public class UpdateContextDataSkeleton<T> : SkeletonBase<Unit, T>, IEcsSkeleton<Unit, T> where T : class
    {
        private readonly Func<T, T> _updateFunc;
        private readonly string? _key;

        public UpdateContextDataSkeleton(Func<T, T> updateFunc, string? key = null, string? name = null, string? desc = null)
            : base(name ?? $"UpdateData<{typeof(T).Name}>", desc ?? $"Updates {typeof(T).Name} in context")
        {
            _updateFunc = updateFunc ?? throw new ArgumentNullException(nameof(updateFunc));
            _key = key;
        }

        protected override T ExecuteCore(Unit input, SkeletonContext ctx)
        {
            var data = ctx.GetData<T>(_key);
            if (data == null)
                throw new InvalidOperationException($"Data of type {typeof(T).Name} not found{(string.IsNullOrEmpty(_key) ? "" : $" with key '{_key}'")} in context.");
            var updated = _updateFunc(data);
            ctx.SetData(updated, _key);
            return updated;
        }

        protected override Task<T> ExecuteCoreAsync(Unit input, SkeletonContext ctx) =>
            Task.FromResult(ExecuteCore(input, ctx));
    }
}
