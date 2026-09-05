#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Common
{
    public interface IMessage
    {
        Guid MessageId { get; }
    }

    public interface IRequest : IMessage
    {
        string Target { get; }
    }

    public interface IResponse : IMessage
    {
        bool Success { get; }
        string? Error { get; }
    }

    public interface IMessageSerializer
    {
        byte[] Serialize(IMessage message);
        IMessage? Deserialize(byte[] data);
    }

    public interface IMessageHandler<in TRequest, TResponse>
        where TRequest : IRequest
        where TResponse : IResponse
    {
        Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
    }

    public interface IMessageTransport : IAsyncDisposable
    {
        Task SendAsync(byte[] data, CancellationToken cancellationToken);
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
        bool IsConnected { get; }
    }

    public class MessageDispatcher<TRequest, TResponse>
        where TRequest : IRequest
        where TResponse : IResponse
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TResponse>> _pendingRequests = new();
        private readonly ConcurrentDictionary<string, Func<TRequest, CancellationToken, Task<TResponse>>> _handlers = new();

        public void RegisterHandler(string target, Func<TRequest, CancellationToken, Task<TResponse>> handler)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers[target] = handler;
        }

        public bool UnregisterHandler(string target) => _handlers.TryRemove(target ?? throw new ArgumentNullException(nameof(target)), out _);

        public Task<TResponse> RegisterPendingRequestAsync(Guid messageId, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(messageId, tcs))
                throw new InvalidOperationException($"Duplicate message id: {messageId}");

            var ctr = cancellationToken.Register(() =>
            {
                if (_pendingRequests.TryRemove(messageId, out var pendingTcs)) pendingTcs.TrySetCanceled(cancellationToken);
            });
            tcs.Task.ContinueWith(_ => ctr.Dispose(), TaskScheduler.Default);
            return tcs.Task;
        }

        public bool TryCompletePendingRequest(TResponse response)
        {
            if (_pendingRequests.TryRemove(response.MessageId, out var tcs))
            {
                tcs.SetResult(response);
                return true;
            }
            return false;
        }

        public void CancelAllPendingRequests()
        {
            var requests = _pendingRequests.ToArray();
            foreach (var kv in requests) kv.Value.TrySetCanceled();
            _pendingRequests.Clear();
        }

        public async Task<TResponse> DispatchAsync(TRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!_handlers.TryGetValue(request.Target, out var handler))
                throw new InvalidOperationException($"No handler registered for target: {request.Target}");
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public abstract class MessageChannel<TRequest, TResponse> : IAsyncDisposable
        where TRequest : IRequest
        where TResponse : IResponse
    {
        protected readonly IMessageTransport _transport;
        protected readonly IMessageSerializer _serializer;
        protected readonly MessageDispatcher<TRequest, TResponse> _dispatcher;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;
        private bool _disposed;

        public bool IsConnected => _transport.IsConnected;

        protected MessageChannel(IMessageTransport transport, IMessageSerializer serializer)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _dispatcher = new MessageDispatcher<TRequest, TResponse>();
        }

        public void StartReceiveLoop(CancellationToken cancellationToken = default)
        {
            if (_receiveTask != null) return;
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        }

        public async Task StopReceiveLoopAsync()
        {
            _receiveCts?.Cancel();
            if (_receiveTask != null) try { await _receiveTask.ConfigureAwait(false); } catch { }
            _receiveCts?.Dispose();
            _receiveCts = null;
            _receiveTask = null;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
                {
                    var data = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    var message = _serializer.Deserialize(data);

                    if (message is TRequest request)
                        _ = Task.Run(() => HandleRequestAsync(request, cancellationToken), cancellationToken);
                    else if (message is TResponse response)
                        _dispatcher.TryCompletePendingRequest(response);
                }
            }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            catch { }
            finally
            {
                _dispatcher.CancelAllPendingRequests();
                OnDisconnected();
            }
        }

        protected abstract Task HandleRequestAsync(TRequest request, CancellationToken cancellationToken);

        protected async Task SendResponseAsync(TResponse response, CancellationToken cancellationToken)
        {
            var data = _serializer.Serialize(response);
            await _transport.SendAsync(data, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TResponse> SendRequestAsync(TRequest request, int timeoutMs, CancellationToken cancellationToken)
        {
            if (!_transport.IsConnected) throw new InvalidOperationException("Channel is not connected.");

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var responseTask = _dispatcher.RegisterPendingRequestAsync(request.MessageId, linkedCts.Token);
            var data = _serializer.Serialize(request);
            await _transport.SendAsync(data, linkedCts.Token).ConfigureAwait(false);

            try { return await responseTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"Request timed out after {timeoutMs}ms");
            }
        }

        public void RegisterHandler(string target, Func<TRequest, CancellationToken, Task<TResponse>> handler)
            => _dispatcher.RegisterHandler(target, handler);

        public bool UnregisterHandler(string target) => _dispatcher.UnregisterHandler(target);

        protected virtual void OnDisconnected() { }

        public virtual async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await StopReceiveLoopAsync().ConfigureAwait(false);
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    public class DynamicInvocationException : Exception
    {
        public DynamicInvocationException(string message) : base(message) { }
        public DynamicInvocationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public interface IDynamicInvoker
    {
        Task<object?> InvokeAsync(object? target, MethodInfo method, object?[] arguments, CancellationToken cancellationToken = default);
    }

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
            Register(new CustomDelegateConverter());
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

            foreach (var converter in _converters)
            {
                if (converter.CanConvert(source, targetType))
                {
                    result = converter.Convert(source, targetType);
                    return true;
                }
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

            return false;
        }

        private class NumericConverter : ITypeConverter
        {
            private static readonly HashSet<Type> NumericTypes = new()
            {
                typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong),
                typeof(float), typeof(double), typeof(decimal)
            };

            public bool CanConvert(object? source, Type targetType)
                => source != null && NumericTypes.Contains(source.GetType()) && NumericTypes.Contains(targetType);

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
                    var instance = Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add")!;
                    foreach (var item in list) addMethod.Invoke(instance, new[] { item });
                    return instance;
                }
            }
        }

        private class CustomDelegateConverter : ITypeConverter
        {
            public bool CanConvert(object? source, Type targetType) => false;
            public object? Convert(object? source, Type targetType) => throw new NotImplementedException();
        }
    }

    public class MethodResolver
    {
        private readonly TypeConverterRegistry _converterRegistry;
        private readonly ConcurrentDictionary<(Type, string, int), MethodInfo[]> _methodCache = new();
        private readonly ConcurrentDictionary<(Type, string, int, string), MethodMatchResult?> _matchCache = new();

        public MethodResolver() : this(new TypeConverterRegistry()) { }
        public MethodResolver(TypeConverterRegistry converterRegistry)
            => _converterRegistry = converterRegistry ?? throw new ArgumentNullException(nameof(converterRegistry));

        public MethodMatchResult Resolve(Type targetType, string methodName, object?[] arguments)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (methodName == null) throw new ArgumentNullException(nameof(methodName));
            arguments ??= Array.Empty<object?>();

            string typeSignature = string.Join("|", arguments.Select(a => a?.GetType().AssemblyQualifiedName ?? "null"));
            var cacheKey = (targetType, methodName, arguments.Length, typeSignature);
            if (_matchCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            var candidates = GetCandidateMethods(targetType, methodName, arguments.Length);
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
                    _matchCache[cacheKey] = result;
                    return result;
                }
            }

            throw new DynamicInvocationException(
                $"No overload of '{methodName}' matches the provided arguments. " +
                $"Argument types: {string.Join(", ", arguments.Select(a => a?.GetType().Name ?? "null"))}. " +
                $"Details: {string.Join("; ", errors)}");
        }

        private MethodInfo[] GetCandidateMethods(Type type, string methodName, int parameterCount)
        {
            var key = (type, methodName, parameterCount);
            return _methodCache.GetOrAdd(key, _ =>
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == methodName && m.GetParameters().Length == parameterCount)
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

    public class DynamicMethodExecutor
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object?, object?[], object?>> _delegateCache = new();

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

    public static class AsyncTaskUnwrapper
    {
        public static async Task<object?> UnwrapAsync(object? result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                if (task.GetType().IsGenericType)
                {
                    return ((dynamic)task).Result;
                }
                return null;
            }
            return result;
        }
    }

    public class ServiceHost
    {
        private readonly ConcurrentDictionary<string, object> _services = new();
        private readonly MethodResolver _resolver;
        private readonly DynamicMethodExecutor _executor;
        private readonly IDynamicInvoker? _customInvoker;

        public ServiceHost() : this(null, null) { }

        public ServiceHost(MethodResolver? resolver = null, DynamicMethodExecutor? executor = null, IDynamicInvoker? customInvoker = null)
        {
            _resolver = resolver ?? new MethodResolver();
            _executor = executor ?? new DynamicMethodExecutor();
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

    public static class ServiceHostExtensions
    {
        public static Task<object?> InvokeAsync(this ServiceHost host, string serviceName, string methodName, params object?[] arguments)
            => host.InvokeAsync(serviceName, methodName, arguments, CancellationToken.None);
    }
}
