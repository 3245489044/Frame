
using System;
using System.Collections.Generic;
using System.Reflection;
using TConfig.ContextFrame.Abstractions;
using BasicMethod.ContextFrame.Core;

namespace BasicMethod.ContextFrame.Builders
{
    public class ContextBuilder<TContext>
        where TContext : class, IContext
    {
        private readonly Func<string?, IContext?, TContext> _factory;
        private string? _id;
        private IContext? _parent;
        private readonly Dictionary<string, object?> _initialData = new();
        private readonly Dictionary<Type, object> _initialServices = new();

        public ContextBuilder(Func<string?, IContext?, TContext> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public ContextBuilder<TContext> WithId(string id)
        {
            _id = id;
            return this;
        }

        public ContextBuilder<TContext> WithParent(IContext parent)
        {
            _parent = parent;
            return this;
        }

        public ContextBuilder<TContext> WithData<T>(T value, string? key = null)
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            _initialData[resolvedKey] = value;
            return this;
        }

        public ContextBuilder<TContext> WithService<T>(T service) where T : class
        {
            _initialServices[typeof(T)] = service;
            return this;
        }

        public TContext Build()
        {
            var context = _factory(_id, _parent);

            foreach (var kvp in _initialData)
            {
                if (kvp.Value != null)
                {
                    InvokeSetData(context, kvp.Value, kvp.Key);
                }
            }

            foreach (var kvp in _initialServices)
            {
                InvokeGetOrAddService(context, kvp.Key, kvp.Value);
            }

            return context;
        }


        private static readonly Dictionary<Type, MethodInfo> _setDataCache = new();
        private static readonly Dictionary<Type, MethodInfo> _getOrAddServiceCache = new();

        private static void InvokeSetData(IContext context, object value, string? key)
        {
            var valueType = value.GetType();

            if (!_setDataCache.TryGetValue(valueType, out var method))
            {
                var setDataMethod = typeof(IContext).GetMethod(nameof(IContext.SetData));
                if (setDataMethod == null)
                    throw new InvalidOperationException("无法找到 IContext.SetData 方法");
                method = setDataMethod.MakeGenericMethod(valueType);
                _setDataCache[valueType] = method;
            }

            method.Invoke(context, new[] { value, key });
        }

        private static void InvokeGetOrAddService(IContext context, Type serviceType, object serviceInstance)
        {
            if (!_getOrAddServiceCache.TryGetValue(serviceType, out var method))
            {
                var getOrAddMethod = typeof(IContext).GetMethod(nameof(IContext.GetOrAddService));
                if (getOrAddMethod == null)
                    throw new InvalidOperationException("无法找到 IContext.GetOrAddService 方法");
                method = getOrAddMethod.MakeGenericMethod(serviceType);
                _getOrAddServiceCache[serviceType] = method;
            }

            var factoryType = typeof(Func<>).MakeGenericType(serviceType);
            var factory = Delegate.CreateDelegate(factoryType, serviceInstance, serviceInstance.GetType().GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!);
            Func<object> wrappedFactory = () => serviceInstance;

            var funcType = typeof(Func<>).MakeGenericType(serviceType);
            var funcDelegate = Delegate.CreateDelegate(funcType, serviceInstance, "GetType");

            var closedMethod = typeof(ContextBuilder<TContext>)
                .GetMethod(nameof(CreateFactoryDelegate), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(serviceType);

            var typedFactory = closedMethod.Invoke(null, new object[] { serviceInstance });

            method.Invoke(context, new[] { typedFactory });
        }

        private static Func<T> CreateFactoryDelegate<T>(T instance) where T : class
        {
            return () => instance;
        }
    }

    public static class ContextBuilder
    {
        public static ContextBuilder<ContextBase> CreateBase()
            => new((id, parent) => new ContextBase(id, parent));

        public static ContextBuilder<PipelineContextBase> CreatePipeline()
            => new((id, parent) => new PipelineContextBase(id, parent));
    }
}
