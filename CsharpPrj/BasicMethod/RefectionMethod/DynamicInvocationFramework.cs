#nullable enable

namespace Modules.ReflectionMethod.Dynamic
{

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Modules.ReflectionCommon;
    using Modules.ReflectionCommon.Caching;

    #region 异常定义

    public class DynamicInvocationException : Exception
    {
        public DynamicInvocationException(string message) : base(message) { }
        public DynamicInvocationException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion

    #region 核心调用接口

    public interface IDynamicInvoker
    {
        Task<object?> InvokeAsync(object? target, MethodInfo method, object?[] arguments, CancellationToken cancellationToken = default);
    }

    #endregion

    #region 方法解析与参数类型转换

    public sealed class MethodMatchResult
    {
        public MethodInfo Method { get; }
        public object?[] ConvertedArguments { get; }

        public MethodMatchResult(MethodInfo method, object?[] convertedArguments)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            ConvertedArguments = convertedArguments ?? throw new ArgumentNullException(nameof(convertedArguments));
        }
    }

    public interface ITypeConverter
    {
        bool CanConvert(object? source, Type targetType);
        object? Convert(object? source, Type targetType);
    }

    public class TypeConverterRegistry
    {
        private readonly List<ITypeConverter> _converters = new();

        public TypeConverterRegistry()
        {
            Register(new NumericConverter());
            Register(new EnumConverter());
            Register(new StringConverter());
            Register(new NullableConverter());
            Register(new CollectionConverter(this));
        }

        public void Register(ITypeConverter converter) => _converters.Add(converter ?? throw new ArgumentNullException(nameof(converter)));

        public bool TryConvert(object? source, Type targetType, out object? result)
        {
            result = null;
            if (source == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return false;
                result = null;
                return true;
            }

            var sourceType = source.GetType();
            if (targetType.IsAssignableFrom(sourceType))
            {
                result = source;
                return true;
            }


            if (source is IConvertible)
            {
                try
                {
                    result = Convert.ChangeType(source, targetType, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                catch { }
            }

            foreach (var converter in _converters)
            {
                if (converter.CanConvert(source, targetType))
                {
                    result = converter.Convert(source, targetType);
                    return true;
                }
            }

            return false;
        }

        private class NumericConverter : ITypeConverter
        {
            public bool CanConvert(object? source, Type targetType)
                => source != null && Modules.ReflectionCommon.TypeExtensions.NumericTypes.Contains(source.GetType()) && Modules.ReflectionCommon.TypeExtensions.NumericTypes.Contains(targetType);

            public object? Convert(object? source, Type targetType)
                => System.Convert.ChangeType(source!, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }

        private class EnumConverter : ITypeConverter
        {
            public bool CanConvert(object? source, Type targetType)
                => targetType.IsEnum && source != null;

            public object? Convert(object? source, Type targetType)
            {
                try { return Enum.Parse(targetType, source!.ToString()!); }
                catch { throw new InvalidCastException($"Cannot convert '{source}' to enum type '{targetType.FullName}'."); }
            }
        }

        private class StringConverter : ITypeConverter
        {
            public bool CanConvert(object? source, Type targetType)
                => targetType == typeof(string);

            public object? Convert(object? source, Type targetType) => source?.ToString();
        }

        private class NullableConverter : ITypeConverter
        {
            public bool CanConvert(object? source, Type targetType)
            {
                var underlying = Nullable.GetUnderlyingType(targetType);
                return underlying != null;
            }

            public object? Convert(object? source, Type targetType)
            {
                var underlying = Nullable.GetUnderlyingType(targetType)!;
                if (source == null) return null;
                var converted = System.Convert.ChangeType(source, underlying, System.Globalization.CultureInfo.InvariantCulture);
                return converted;
            }
        }

        private class CollectionConverter : ITypeConverter
        {
            private readonly TypeConverterRegistry _registry;
            public CollectionConverter(TypeConverterRegistry registry) => _registry = registry;

            public bool CanConvert(object? source, Type targetType)
            {
                if (source is not System.Collections.IEnumerable) return false;
                if (targetType.IsArray) return true;
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)) return true;
                return false;
            }

            public object? Convert(object? source, Type targetType)
            {
                var enumerable = (System.Collections.IEnumerable)source!;
                var elementType = targetType.IsArray ? targetType.GetElementType()! : targetType.GetGenericArguments()[0];
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    if (!_registry.TryConvert(item, elementType, out var converted))
                        throw new InvalidCastException($"Cannot convert collection element '{item}' to {elementType.FullName}.");
                    list.Add(converted);
                }

                if (targetType.IsArray)
                {
                    var array = Array.CreateInstance(elementType, list.Count);
                    for (int i = 0; i < list.Count; i++) array.SetValue(list[i], i);
                    return array;
                }
                else
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var instance = Activator.CreateInstance(listType)
                        ?? throw new InvalidOperationException($"Failed to create instance of {listType.FullName}");
                    var addMethod = listType.GetMethod("Add")!;
                    foreach (var item in list) addMethod.Invoke(instance, new[] { item });
                    return instance;
                }
            }
        }

    }

    public class MethodResolver
    {
        private readonly TypeConverterRegistry _converterRegistry;
        private readonly Cache<(Type, string, int), MethodInfo[]> _methodCache;
        private readonly Cache<(Type, string, int, string), MethodMatchResult?> _matchCache;

        public MethodResolver() : this(new TypeConverterRegistry()) { }
        public MethodResolver(TypeConverterRegistry converterRegistry)
        {
            _converterRegistry = converterRegistry ?? throw new ArgumentNullException(nameof(converterRegistry));
            _methodCache = new Cache<(Type, string, int), MethodInfo[]>();
            _matchCache = new Cache<(Type, string, int, string), MethodMatchResult?>();
        }

        public MethodMatchResult Resolve(Type targetType, string methodName, object?[] arguments)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (methodName == null) throw new ArgumentNullException(nameof(methodName));
            arguments ??= Array.Empty<object?>();

            string typeSignature = string.Join("|", arguments.Select(a => a?.GetType().AssemblyQualifiedName ?? "null"));
            var cacheKey = (targetType, methodName, arguments.Length, typeSignature);
            if (_matchCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            var candidates = GetCandidateMethods(targetType, methodName);
            if (candidates.Length == 0)
                throw new MissingMethodException($"No public method named '{methodName}' found in type '{targetType.FullName}'.");

            candidates = candidates.Where(m =>
            {
                var ps = m.GetParameters();
                int required = ps.Count(p => !p.IsOptional);
                return arguments.Length >= required && arguments.Length <= ps.Length;
            }).ToArray();

            if (candidates.Length == 0)
                throw new MissingMethodException($"No public method named '{methodName}' with {arguments.Length} parameters found in type '{targetType.FullName}'.");

            candidates = SortByParameterCompatibility(candidates, arguments);

            var errors = new List<string>();
            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                var converted = new object?[parameters.Length];
                bool success = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i >= arguments.Length)
                    {
                        if (parameters[i].HasDefaultValue)
                        {
                            converted[i] = parameters[i].DefaultValue;
                            continue;
                        }
                        success = false;
                        errors.Add($"Parameter '{parameters[i].Name}' (index {i}): missing required argument.");
                        break;
                    }

                    if (!_converterRegistry.TryConvert(arguments[i], parameters[i].ParameterType, out converted[i]))
                    {
                        success = false;
                        errors.Add($"Parameter '{parameters[i].Name}' (index {i}): cannot convert from '{arguments[i]?.GetType().FullName ?? "null"}' to '{parameters[i].ParameterType.FullName}'.");
                        break;
                    }
                }

                if (success)
                {
                    var result = new MethodMatchResult(method, converted);
                    _matchCache.Set(cacheKey, result);
                    return result;
                }
            }

            throw new DynamicInvocationException(
                $"No overload of '{methodName}' matches the provided arguments. " +
                $"Argument types: {string.Join(", ", arguments.Select(a => a?.GetType().Name ?? "null"))}. " +
                $"Details: {string.Join("; ", errors)}");
        }

        private MethodInfo[] GetCandidateMethods(Type type, string methodName)
        {
            var key = (type, methodName, -1);
            return _methodCache.GetOrAdd(key, _ =>
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == methodName)
                    .ToArray();
                return methods;
            });
        }

        private static MethodInfo[] SortByParameterCompatibility(MethodInfo[] methods, object?[] arguments)
        {
            return methods.OrderByDescending(m =>
            {
                var score = 0;
                var parameters = m.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i >= arguments.Length) break;
                    if (arguments[i] == null) continue;
                    var argType = arguments[i]!.GetType();
                    var paramType = parameters[i].ParameterType;
                    if (argType == paramType) score += 100;
                    else if (paramType.IsAssignableFrom(argType)) score += 50;
                }
                return score;
            }).ToArray();
        }

        public void ClearCache() => _matchCache.Clear();
    }

    #endregion

    #region 委托编译执行器

    public class DynamicMethodExecutor
    {
        private static readonly Cache<MethodInfo, Func<object?, object?[], object?>> _delegateCache;

        static DynamicMethodExecutor()
        {
            _delegateCache = new Cache<MethodInfo, Func<object?, object?[], object?>>();
        }

        public static Func<object[], Task<object>> CreateExecutor(MethodInfo method)
        {
            return CreateExecutorInternal(method, null);
        }

        public static Func<object[], Task<object>> CreateExecutor(MethodInfo method, object target)
        {
            return CreateExecutorInternal(method, target);
        }

        private static Func<object[], Task<object>> CreateExecutorInternal(MethodInfo method, object? target)
        {
            var parameters = method.GetParameters();
            var paramTypes = parameters.Select(p => p.ParameterType).ToArray();

            var argsParam = Expression.Parameter(typeof(object[]), "args");
            var convertedArgs = new Expression[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                convertedArgs[i] = Expression.Convert(
                    Expression.ArrayIndex(argsParam, Expression.Constant(i)),
                    paramTypes[i]);
            }

            Expression callExpr;
            if (method.IsStatic)
            {
                callExpr = Expression.Call(method, convertedArgs);
            }
            else if (target != null)
            {
                var instanceExpr = Expression.Constant(target);
                callExpr = Expression.Call(instanceExpr, method, convertedArgs);
            }
            else
            {
                throw new InvalidOperationException(
                    "Instance methods require a target instance. " +
                    "Use CreateExecutor(MethodInfo, object) overload instead.");
            }

            return WrapReturnType(callExpr, method.ReturnType, argsParam);
        }

        internal static Func<object[], Task<object>> WrapReturnType(Expression callExpr, Type returnType, ParameterExpression argsParam)
        {
            if (returnType == typeof(void))
            {
                var lambda = Expression.Lambda<Action<object[]>>(callExpr, argsParam);
                var action = lambda.Compile();
                return args => { action(args); return Task.FromResult<object>(null!); };
            }
            if (returnType == typeof(Task))
            {
                var lambda = Expression.Lambda<Func<object[], Task>>(callExpr, argsParam);
                var func = lambda.Compile();
                return async args => { await func(args); return null!; };
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var getResultMethod = typeof(DynamicMethodExecutor).GetMethod(
                    nameof(GetTaskResult), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(resultType);
                var taskObjExpr = Expression.Call(getResultMethod, callExpr);
                var lambda = Expression.Lambda<Func<object[], Task<object>>>(taskObjExpr, argsParam);
                return lambda.Compile();
            }
            {
                var resultExpr = Expression.Convert(callExpr, typeof(object));
                var lambda = Expression.Lambda<Func<object[], object>>(resultExpr, argsParam);
                var func = lambda.Compile();
                return args => Task.FromResult(func(args));
            }
        }

        internal static async Task<object> GetTaskResult<T>(Task<T> task) => await task!;

        public object? Execute(MethodInfo method, object? target, object?[] arguments)
        {
            var invoker = _delegateCache.GetOrAdd(method, CreateInvokerDelegate);
            return invoker(target, arguments);
        }

        private static Func<object?, object?[], object?> CreateInvokerDelegate(MethodInfo method)

        {
            var targetParam = Expression.Parameter(typeof(object), "target");
            var argsParam = Expression.Parameter(typeof(object[]), "args");

            var parameters = method.GetParameters();
            var argExprs = new Expression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var arg = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                argExprs[i] = Expression.Convert(arg, parameters[i].ParameterType);
            }

            Expression call;
            if (method.IsStatic)
            {
                call = Expression.Call(method, argExprs);
            }
            else
            {
                var instance = Expression.Convert(targetParam, method.DeclaringType!);
                call = Expression.Call(instance, method, argExprs);
            }

            if (method.ReturnType == typeof(void))
            {
                call = Expression.Block(call, Expression.Constant(null, typeof(object)));
            }
            else
            {
                call = Expression.Convert(call, typeof(object));
            }

            var lambda = Expression.Lambda<Func<object?, object?[], object?>>(call, targetParam, argsParam);
            return lambda.Compile();
        }

        public static void ClearCache() => _delegateCache.Clear();
    }

    public static class DelegateCompiler
    {
        public static Func<object[], Task<object>> Compile(Delegate function)
        {
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            var method = function.Method;
            var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

            var argsParam = Expression.Parameter(typeof(object[]), "args");
            var convertedArgs = new Expression[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                convertedArgs[i] = Expression.Convert(
                    Expression.ArrayIndex(argsParam, Expression.Constant(i)),
                    paramTypes[i]);
            }

            var invokeExpr = Expression.Invoke(Expression.Constant(function), convertedArgs);

            var returnType = method.ReturnType;
            if (returnType == typeof(ValueTask))
            {
                var lambda = Expression.Lambda<Func<object[], ValueTask>>(invokeExpr, argsParam);
                var func = lambda.Compile();
                return async args => { await func(args); return null!; };
            }

            if (returnType.IsGenericType && typeof(ValueTask<>) == returnType.GetGenericTypeDefinition())
            {
                var resultType = returnType.GetGenericArguments()[0];
                var getResultMethod = typeof(DelegateCompiler).GetMethod(
                    nameof(GetValueTaskResult), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(resultType);
                var taskObjExpr = Expression.Call(getResultMethod, invokeExpr);
                var lambda = Expression.Lambda<Func<object[], Task<object>>>(taskObjExpr, argsParam);
                return lambda.Compile();
            }

            return DynamicMethodExecutor.WrapReturnType(invokeExpr, returnType, argsParam);
        }

        private static async Task<object> GetValueTaskResult<T>(ValueTask<T> task) => await task;
    }

    #endregion

    #region 异步任务解包器

    public static class AsyncTaskUnwrapper
    {
        public static async Task<object?> UnwrapAsync(object? result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                if (task.GetType().IsGenericType)
                {
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                }
                return null;
            }
            return result;
        }
    }

    #endregion

    #region 服务注册与动态调用

    public class ServiceHost
    {
        private readonly ConcurrentDictionary<string, object> _services = new();
        private readonly MethodResolver _resolver;
        private readonly DynamicMethodExecutor _executor;
        private readonly IDynamicInvoker? _customInvoker;

        public ServiceHost() : this(new MethodResolver(), new DynamicMethodExecutor()) { }

        public ServiceHost(MethodResolver resolver, DynamicMethodExecutor executor, IDynamicInvoker? customInvoker = null)
        {
            // 必填端口（T）：反射解析器/执行器由装配点显式提供；无参构造是组件自装配的唯一入口。
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _customInvoker = customInvoker;
        }

        public void RegisterService(string name, object instance)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!_services.TryAdd(name, instance))
                throw new InvalidOperationException($"Service '{name}' is already registered.");
        }

        public bool UnregisterService(string name) => _services.TryRemove(name, out _);

        public async Task<object?> InvokeAsync(string serviceName, string methodName, object?[] arguments, CancellationToken cancellationToken = default)
        {
            if (!_services.TryGetValue(serviceName, out var instance))
                throw new InvalidOperationException($"Service '{serviceName}' not found.");

            var match = _resolver.Resolve(instance.GetType(), methodName, arguments);

            if (_customInvoker != null)
            {
                return await _customInvoker.InvokeAsync(instance, match.Method, match.ConvertedArguments, cancellationToken);
            }

            var rawResult = _executor.Execute(match.Method, instance, match.ConvertedArguments);
            return await AsyncTaskUnwrapper.UnwrapAsync(rawResult);
        }

        public MethodResolver Resolver => _resolver;
        public DynamicMethodExecutor Executor => _executor;
    }

    #endregion

    #region 强类型代理生成（基于 DispatchProxy）

    public static class ProxyFactory
    {
        private static readonly Cache<Type, Type> _proxyTypeCache;

        static ProxyFactory()
        {
            _proxyTypeCache = new Cache<Type, Type>();
        }

        public static TInterface Create<TInterface>(Func<string, object?[], CancellationToken, Task<object?>> invoker)
            where TInterface : class
        {
            if (!typeof(TInterface).IsInterface)
                throw new ArgumentException("TInterface must be an interface type.");

            var proxyType = _proxyTypeCache.GetOrAdd(typeof(TInterface), _ =>
            {
                return DispatchProxyGenerator.GenerateProxyType(typeof(TInterface));
            });

            var proxy = (TInterface)Activator.CreateInstance(proxyType)!;
            ((IProxyInvoker)proxy).SetInvoker(invoker);
            return proxy;
        }

        private interface IProxyInvoker
        {
            void SetInvoker(Func<string, object?[], CancellationToken, Task<object?>> invoker);
        }

        private static class DispatchProxyGenerator
        {
            public static Type GenerateProxyType(Type interfaceType)
            {
                var genericProxyType = typeof(GenericProxy<>).MakeGenericType(interfaceType);
                return genericProxyType;
            }
        }

        private class GenericProxy<T> : DispatchProxy, IProxyInvoker where T : class
        {
            private Func<string, object?[], CancellationToken, Task<object?>>? _invoker;

            public void SetInvoker(Func<string, object?[], CancellationToken, Task<object?>> invoker) => _invoker = invoker;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (_invoker == null) throw new InvalidOperationException("Proxy not initialized.");
                if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));

                var methodName = targetMethod.Name;
                var safeArgs = args ?? Array.Empty<object?>();
                var cancellationToken = ExtractCancellationToken(safeArgs) ?? CancellationToken.None;

                var filteredArgs = RemoveCancellationToken(safeArgs);

                var task = _invoker(methodName, filteredArgs, cancellationToken);

                var returnType = targetMethod.ReturnType;
                if (returnType == typeof(void))
                {
                    task.GetAwaiter().GetResult();
                    return null;
                }
                if (returnType == typeof(Task))
                {
                    return task;
                }
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultType = returnType.GetGenericArguments()[0];
                    return ConvertTaskResult(task, resultType);
                }

                return task.GetAwaiter().GetResult();
            }

            private CancellationToken? ExtractCancellationToken(object?[] args)
            {
                foreach (var arg in args)
                    if (arg is CancellationToken ct) return ct;
                return null;
            }

            private static object?[] RemoveCancellationToken(object?[] args)
            {
                if (args == null || args.Length == 0)
                    return Array.Empty<object?>();

                var filtered = new List<object?>(args.Length);
                foreach (var arg in args)
                {
                    if (arg is CancellationToken) continue;
                    filtered.Add(arg);
                }
                return filtered.ToArray();
            }

            private static object ConvertTaskResult(Task<object?> task, Type resultType)
            {
                var method = typeof(ProxyFactory).GetMethod(nameof(ConvertTaskResultInternal), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(resultType);
                return method.Invoke(null, new object[] { task })!;
            }
        }

        private static async Task<TResult> ConvertTaskResultInternal<TResult>(Task<object?> task)
        {
            var result = await task.ConfigureAwait(false);
            if (result == null && default(TResult) != null)
                throw new InvalidOperationException($"Cannot convert null to {typeof(TResult).Name}");
            return (TResult)result!;
        }
    }

    #endregion

    #region 服务扩展方法

    public static class ServiceHostExtensions
    {
        public static Task<object?> InvokeAsync(this ServiceHost host, string serviceName, string methodName, params object?[] arguments)
            => host.InvokeAsync(serviceName, methodName, arguments, CancellationToken.None);
    }

    #endregion
}
