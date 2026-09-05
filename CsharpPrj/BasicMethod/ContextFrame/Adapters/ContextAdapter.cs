
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Adapters
{
    public abstract class ContextAdapter<TSource, TTarget>
        where TSource : class, TConfig.ContextFrame.Abstractions.IContext
        where TTarget : class, TConfig.ContextFrame.Abstractions.IContext
    {
        protected TSource Source { get; }

        protected ContextAdapter(TSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public abstract TTarget Adapt();


        protected void ForwardSetData<T>(T value, string? key = null) where T : notnull
            => Source.SetData(value, key);

        protected T? ForwardGetData<T>(string? key = null) where T : notnull
            => Source.GetData<T>(key);

        protected bool ForwardTryGetData<T>([MaybeNullWhen(false)] out T? value, string? key = null) where T : notnull
            => Source.TryGetData(out value, key);

        protected interface ITarget
        {
            TTarget Adapt();
        }
    }

    public class SimpleContextAdapter : IContext
    {
        private readonly IContext _source;

        public SimpleContextAdapter(IContext source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string Id => _source.Id;

        public IContext? Parent => _source.Parent;

        public bool IsDisposed => _source.IsDisposed;

        public DateTime CreatedAt => _source.CreatedAt;

        public CancellationToken CancellationToken => _source.CancellationToken;

        public event EventHandler<ContextDataChangedEventArgs>? DataChanged
        {
            add => _source.DataChanged += value;
            remove => _source.DataChanged -= value;
        }

        public void SetData<T>(T value, string? key = null) where T : notnull => _source.SetData(value, key);

        public T? GetData<T>(string? key = null) where T : notnull => _source.GetData<T>(key);

        public bool TryGetData<T>([MaybeNullWhen(false)] out T? value, string? key = null) where T : notnull => _source.TryGetData(out value, key);

        public bool RemoveData<T>(string? key = null) where T : notnull => _source.RemoveData<T>(key);

        public bool ContainsData<T>(string? key = null) where T : notnull => _source.ContainsData<T>(key);

        public IEnumerable<T> GetAllData<T>() where T : notnull => _source.GetAllData<T>();

        public IEnumerable<object> GetAllData() => _source.GetAllData();

        public T GetOrAddService<T>(Func<T> factory) where T : notnull => _source.GetOrAddService(factory);

        public bool TryGetService<T>([MaybeNullWhen(false)] out T? service) where T : notnull => _source.TryGetService(out service);

        public void Dispose() => _source.Dispose();

        public ValueTask DisposeAsync() => _source.DisposeAsync();
    }
}
