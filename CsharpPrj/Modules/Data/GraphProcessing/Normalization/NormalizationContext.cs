
#nullable enable

using Modules.Transformers;

namespace Modules.GraphProcessing.Normalization
{
    public interface INormalizationContext
    {
        T GetService<T>() where T : class;

        void SetService<T>(T service) where T : class;

        T GetOrAddService<T>(Func<T> factory) where T : class;

        bool TryGetData<T>(out T value, string? key = null) where T : class;

        void SetData<T>(T value, string? key = null) where T : class;

        void Log(string message);

        IDisposable BeginScope();
    }

    public class DefaultNormalizationContext : INormalizationContext
    {
        public static INormalizationContext? Current
        {
            get
            {
                var ctx = TransformContext.Current;
                return ctx != null ? new DefaultNormalizationContext(ctx) : null;
            }
        }

        private readonly TransformContext _inner;

        public DefaultNormalizationContext()
        {
            _inner = new TransformContext();
        }

        internal DefaultNormalizationContext(TransformContext inner)
        {
            _inner = inner;
        }

        public T GetService<T>() where T : class
        {
            return _inner.Get<T>("service:" + typeof(T).FullName)
                ?? throw new KeyNotFoundException($"Service '{typeof(T).FullName}' not registered.");
        }

        public void SetService<T>(T service) where T : class
        {
            _inner.Set("service:" + typeof(T).FullName, service!);
        }

        public T GetOrAddService<T>(Func<T> factory) where T : class
        {
            var key = "service:" + typeof(T).FullName;
            if (_inner.TryGet<T>(key, out var existing))
                return existing!;

            var service = factory();
            _inner.Set(key, service!);
            return service;
        }

        public bool TryGetData<T>(out T value, string? key = null) where T : class
        {
            key ??= "data:" + typeof(T).FullName;
            return _inner.TryGet(key, out value);
        }

        public void SetData<T>(T value, string? key = null) where T : class
        {
            _inner.Set(key ?? "data:" + typeof(T).FullName, value!);
        }

        public void Log(string message)
        {
            _inner.Log(message);
        }

        public IDisposable BeginScope()
        {
            return TransformContext.BeginScope(ctx =>
            {
                foreach (var kvp in _inner.Items)
                    ctx.Set(kvp.Key, kvp.Value);
            });
        }

        public static IDisposable BeginScope(INormalizationContext? parent = null)
        {
            if (parent is DefaultNormalizationContext dnc)
            {
                return TransformContext.BeginScope(ctx =>
                {
                    foreach (var kvp in dnc._inner.Items)
                        ctx.Set(kvp.Key, kvp.Value);
                });
            }

            return TransformContext.BeginScope();
        }
    }
}
