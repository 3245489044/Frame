#nullable enable
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Core;
using UniversalMiddleware.Serialization;

using JP = Plate.JsonMethod;

namespace UniversalMiddleware.Messaging
{
    public class Mediator : Abstractions.IMediator
    {
        private readonly MediatR.IMediator _mediatr;
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;
        private readonly ICacheManager _cache;
        private readonly IStateManager _state;

        public ICacheManager Cache => _cache;
        public IStateManager State => _state;

        public Mediator(
            ICacheManager? cache = null,
            IStateManager? state = null,
            Microsoft.Extensions.Logging.ILogger? logger = null,
            Action<IServiceCollection>? configureServices = null)
        {
            _cache = cache ?? new CacheManager();
            _state = state ?? new StateManager();
            _logger = logger;

            var services = new ServiceCollection();

            configureServices?.Invoke(services);

            services.AddLogging();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(Mediator).Assembly);
            });
            var provider = services.BuildServiceProvider();
            _mediatr = provider.GetRequiredService<MediatR.IMediator>();
        }

        public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            try
            {
                _logger?.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Mediator.Send<{typeof(TRequest).Name}, {typeof(TResponse).Name}>");
                var result = await _mediatr.Send(request, cancellationToken);
                return (TResponse)result!;
            }
            catch (Exception ex)
            {
                _logger?.Log(Microsoft.Extensions.Logging.LogLevel.Error, $"Send<{typeof(TRequest).Name}> failed: {ex.Message}", ex);
                throw;
            }
        }

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : class
        {
            try
            {
                _logger?.Log(Microsoft.Extensions.Logging.LogLevel.Debug, $"Mediator.Publish<{typeof(TEvent).Name}>");

                await _mediatr.Publish(@event);
            }
            catch (Exception ex)
            {
                _logger?.Log(Microsoft.Extensions.Logging.LogLevel.Error, $"Publish<{typeof(TEvent).Name}> failed: {ex.Message}", ex);
                throw;
            }
        }

        [Obsolete("RegisterHandler is not supported. Register IRequestHandler<TRequest, TResponse> via MediatR DI instead.")]
        public void RegisterHandler<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<TResponse>> handler)
            where TRequest : class
            where TResponse : class
        {
            throw new NotSupportedException(
                $"RegisterHandler is not supported. Register IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}> via MediatR DI instead.");
        }

        [Obsolete("RegisterEventHandler is not supported. Register INotificationHandler<TEvent> via MediatR DI instead.")]
        public Action RegisterEventHandler<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            throw new NotSupportedException(
                $"RegisterEventHandler is not supported. Register INotificationHandler<{typeof(TEvent).Name}> via MediatR DI instead.");
        }

        public void UnregisterAll()
        {
            _cache.Clear();
            _state.ClearState();
        }
    }

    public class CacheManager : ICacheManager
    {
        private readonly ConcurrentDictionary<string, (object? Value, DateTime? ExpiresAt)> _cache = new();

        public T? Get<T>(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt == null || entry.ExpiresAt > DateTime.UtcNow)
                    return (T?)entry.Value;
                _cache.TryRemove(key, out _);
            }
            return default;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            _cache[key] = (value, expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null);
        }

        public void Remove(string key) => _cache.TryRemove(key, out _);
        public void Clear() => _cache.Clear();
        public int GetCount() => _cache.Count;
        public bool Contains(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                return entry.ExpiresAt == null || entry.ExpiresAt > DateTime.UtcNow;
            }
            return false;
        }
    }

    public class StateManager : IStateManager
    {
        private readonly ConcurrentDictionary<string, object?> _state = new();

        public event EventHandler<StateChangedEventArgs>? StateChanged;

        public T? GetState<T>(string key)
            => _state.TryGetValue(key, out var value) ? (T?)value : default;

        public void SetState<T>(string key, T value)
        {
            var oldValue = _state.TryGetValue(key, out var existing) ? existing : null;
            _state[key] = value;
            StateChanged?.Invoke(this, new StateChangedEventArgs(key, oldValue, value));
        }

        public void RemoveState(string key)
        {
            if (_state.TryRemove(key, out var existing))
            {
                StateChanged?.Invoke(this, new StateChangedEventArgs(key, existing, null));
            }
        }

        public void ClearState() => _state.Clear();
        public bool ContainsState(string key) => _state.ContainsKey(key);
    }

    public class ConsoleLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _inner;

        private static readonly Lazy<Microsoft.Extensions.Logging.ILoggerFactory> _defaultFactory = new(() =>
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            }));

        public ConsoleLogger(string categoryName = "Universal")
        {
            _inner = _defaultFactory.Value.CreateLogger(categoryName);
        }

        public ConsoleLogger(Microsoft.Extensions.Logging.ILoggerFactory factory, string categoryName = "Universal")
        {
            _inner = factory.CreateLogger(categoryName);
        }


        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
            => _inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);


    }
}

namespace UniversalMiddleware.Layout
{
    using UniversalMiddleware.Abstractions;


    public struct Rectangle
    {
        public int X, Y, Width, Height;
        public override string ToString() => $"({X},{Y} {Width}x{Height})";
    }

    public class LayoutCalculator : ILayoutCalculator
    {
        public LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input)
        {
            if (definition.Elements.Count == 0)
                return LayoutOutput.Failure("No elements to calculate");

            var outputs = new List<ElementOutput>();
            foreach (var element in definition.Elements)
            {
                var output = CalculateElement(element, input);
                outputs.Add(output);
            }
            return LayoutOutput.Success(outputs);
        }

        public ElementOutput CalculateElement(ElementDefinition element, LayoutInput input)
        {
            var pos = element.Position;
            int x, y, w, h;


            if (pos.Width >= 0 && pos.Width <= 1)
                w = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentWidth, pos.Width);
            else if (pos.Width < 0)
                w = input.Width;
            else
                w = input.Width;

            if (pos.Height >= 0 && pos.Height <= 1)
                h = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentHeight, pos.Height);
            else if (pos.Height < 0)
                h = input.Height;
            else
                h = input.Height;

            w = Math.Max(element.SizeConstraints.MinWidth, Math.Min(element.SizeConstraints.MaxWidth, w > 0 ? w : input.Width));
            h = Math.Max(element.SizeConstraints.MinHeight, Math.Min(element.SizeConstraints.MaxHeight, h > 0 ? h : input.Height));

            if (pos.Left >= 0 && pos.Left <= 1)
                x = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentWidth, pos.Left);
            else if (pos.Left < 0)
            {
                double rightRatio = -pos.Left;
                int rightPx = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentWidth, rightRatio);
                x = input.ParentWidth - rightPx - w;
            }
            else
                x = input.X;

            if (pos.Top >= 0 && pos.Top <= 1)
                y = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentHeight, pos.Top);
            else if (pos.Top < 0)
            {
                double bottomRatio = -pos.Top;
                int bottomPx = global::UniversalLayoutMath.ProportionalMath.RatioToPixel(input.ParentHeight, bottomRatio);
                y = input.ParentHeight - bottomPx - h;
            }
            else
                y = input.Y;

            return new ElementOutput
            {
                ControlName = element.ControlName,
                X = x, Y = y, Width = w, Height = h,
                ZOrder = element.ZOrder.Order
            };
        }
    }

    public class LayoutValidator : ILayoutValidator
    {
        private readonly LayoutCalculator _calculator;

        public LayoutValidator(LayoutCalculator? calculator = null)
        {
            _calculator = calculator ?? new LayoutCalculator();
        }

        public JsonValidationResult ValidateDefinition(LayoutDefinition definition)
            => ValidateDefinition(definition, 1000, 1000);

        public JsonValidationResult ValidateDefinition(LayoutDefinition definition, int parentWidth, int parentHeight)
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
                return JsonValidationResult.Failure("Layout name cannot be empty");

            if (definition.Elements.Count == 0)
                return JsonValidationResult.Failure("Layout must contain at least one element");

            var constraintResult = ValidateConstraints(definition.Constraints);
            if (!constraintResult.IsValid) return constraintResult;

            foreach (var element in definition.Elements)
            {
                var elementResult = ValidateElement(element, definition);
                if (!elementResult.IsValid) return elementResult;
            }

            if (definition.Elements.Count >= 2)
            {
                var input = new LayoutInput(0, 0, parentWidth, parentHeight, parentWidth, parentHeight);

                for (int i = 0; i < definition.Elements.Count; i++)
                {
                    for (int j = i + 1; j < definition.Elements.Count; j++)
                    {
                        var e1 = definition.Elements[i];
                        var e2 = definition.Elements[j];
                        if (e1.ZOrder.Order == e2.ZOrder.Order)
                        {
                            var output1 = _calculator.CalculateElement(e1, input);
                            var output2 = _calculator.CalculateElement(e2, input);

                            var r1 = new global::UniversalLayoutMath.Rect(output1.X, output1.Y, output1.Width, output1.Height);
                            var r2 = new global::UniversalLayoutMath.Rect(output2.X, output2.Y, output2.Width, output2.Height);

                            var overlapRect = global::UniversalLayoutMath.OverlapMath.CalculateOverlapRect(r1, r2);

                            if (overlapRect.Area > 0)
                            {
                                double ratio = global::UniversalLayoutMath.OverlapMath.CalculateOverlapRatio(r1, r2, overlapRect);
                                var level = global::UniversalLayoutMath.OverlapMath.ClassifyOverlap(ratio);
                                string levelDesc = level switch
                                {
                                    global::UniversalLayoutMath.OverlapLevel.None => "无重叠",
                                    global::UniversalLayoutMath.OverlapLevel.Slight => "轻微重叠",
                                    global::UniversalLayoutMath.OverlapLevel.Moderate => "中度重叠",
                                    global::UniversalLayoutMath.OverlapLevel.Severe => "严重重叠",
                                    global::UniversalLayoutMath.OverlapLevel.Complete => "完全重叠",
                                    _ => "未知"
                                };

                                if (level >= global::UniversalLayoutMath.OverlapLevel.Severe)
                                {
                                    return JsonValidationResult.Failure(
                                        $"'{e1.ControlName}' and '{e2.ControlName}' severely overlap at same Z-order " +
                                        $"(重叠率: {ratio:P1}, 级别: {levelDesc})");
                                }
                                else
                                {
                                    System.Diagnostics.Trace.WriteLine(
                                        $"[LayoutValidator] '{e1.ControlName}' and '{e2.ControlName}' {levelDesc} at same Z-order " +
                                        $"(重叠率: {ratio:P1})");
                                }
                            }
                        }
                    }
                }
            }

            return JsonValidationResult.Success();
        }

        public JsonValidationResult ValidateElement(ElementDefinition element, LayoutDefinition context)
        {
            if (string.IsNullOrWhiteSpace(element.ControlName))
                return JsonValidationResult.Failure("Element control name cannot be empty");

            var pos = element.Position;
            if (pos.Left < -1 || pos.Left > 1 || pos.Top < -1 || pos.Top > 1 ||
                pos.Width < -1 || pos.Width > 1 || pos.Height < -1 || pos.Height > 1)
                return JsonValidationResult.Failure($"Position values for '{element.ControlName}' must be in [-1, 1]");

            if (context.Type == global::UniversalLayoutFramework.LayoutType.Proportional)

            {
                if (pos.Left >= 0 && pos.Width >= 0 && pos.Left + pos.Width > 1)
                    return JsonValidationResult.Failure($"'{element.ControlName}' horizontal layout exceeds parent bounds");
                if (pos.Top >= 0 && pos.Height >= 0 && pos.Top + pos.Height > 1)
                    return JsonValidationResult.Failure($"'{element.ControlName}' vertical layout exceeds parent bounds");
            }

            return JsonValidationResult.Success();
        }

        public JsonValidationResult ValidateConstraints(LayoutConstraints constraints)
        {
            if (constraints.MinWidth < 0 || constraints.MinHeight < 0)
                return JsonValidationResult.Failure("Minimum dimensions cannot be negative");
            if (constraints.MaxWidth > 0 && constraints.MinWidth > constraints.MaxWidth)
                return JsonValidationResult.Failure("MinWidth cannot exceed MaxWidth");
            if (constraints.MaxHeight > 0 && constraints.MinHeight > constraints.MaxHeight)
                return JsonValidationResult.Failure("MinHeight cannot exceed MaxHeight");
            return JsonValidationResult.Success();
        }
    }
}

namespace UniversalMiddleware.Diagnostics
{
    using UniversalMiddleware.Abstractions;
    using Modules.Pipeline;
    using JPDA = Plate.JsonMethod.Diagnostics;

    public static class JsonDiagnostics
    {
        public static async Task<DiagnosticResult> MeasureAsync<TInput, TOutput>(
            IPipeline<TInput, TOutput> pipeline, TInput input)
        {
            var sw = Stopwatch.StartNew();
            var memBefore = GC.GetTotalMemory(true);
            var result = await pipeline.ExecuteAsync(input);
            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);

            return new DiagnosticResult
            {
                PipelineName = pipeline.Name,
                DurationMs = sw.ElapsedMilliseconds,
                MemoryDeltaBytes = memAfter - memBefore,
                Success = result != null
            };
        }

        public static JsonSizeInfo AnalyzeJsonSize(string json)
        {
            var jpResult = JPDA.JsonSizeAnalyzer.AnalyzeJsonSize(json);
            return new JsonSizeInfo
            {
                RawBytes = jpResult.RawBytes,
                ElementCount = jpResult.ElementCount,
                EstimatedKb = jpResult.EstimatedKb
            };
        }
    }

    public class DiagnosticResult
    {
        public string PipelineName { get; set; } = "";
        public long DurationMs { get; set; }
        public long MemoryDeltaBytes { get; set; }
        public bool Success { get; set; }
        public string MemoryDeltaFormat => MemoryDeltaBytes >= 1024
            ? $"{MemoryDeltaBytes / 1024.0:F2} KB"
            : $"{MemoryDeltaBytes} B";
    }

    public class JsonSizeInfo
    {
        public long RawBytes { get; set; }
        public int ElementCount { get; set; }
        public double EstimatedKb { get; set; }
        public string EstimatedMb => $"{EstimatedKb / 1024.0:F2} MB";
    }
}

namespace UniversalMiddleware.Extensions
{
    using Modules.Pipeline;
    using TConfig.Abstractions;
    using Plate.JsonMethod.Extensions;

    public static class StringJsonExtensions
    {
        public static T? FromJson<T>(this string json, JsonSerializerOptions? options = null)
            => JsonExtensions.FromJson<T>(json, options);

        public static bool IsValidJson(this string json)
            => JsonExtensions.IsValidJson(json);

        public static string PrettyPrintJson(this string json)
            => JsonExtensions.PrettyPrintJson(json);

        public static string MinifyJson(this string json)
            => JsonExtensions.MinifyJson(json);
    }

    public static class ObjectJsonExtensions
    {
        public static string ToJson<T>(this T obj, bool writeIndented = true)
            => JsonExtensions.ToJson(obj, writeIndented);

        public static string ToSafeJson<T>(this T obj)
            => JsonExtensions.ToSafeJson(obj);

        public static JsonNode? ToJsonNode<T>(this T obj)
            => JsonExtensions.ToJsonNode(obj);

        public static T? DeepCloneViaJson<T>(this T obj)
            => JsonExtensions.DeepCloneViaJson(obj);
    }

    public static class PipelineExtensions
    {
        public static JsonPipelineBuilder<TInput, TOutput> WithDefaultSerialization<TInput, TOutput>(
            this JsonPipelineBuilder<TInput, TOutput> builder)
        {
            return builder.AddTransform(_ => new JsonParseTransform());
        }

        public static JsonPipelineBuilder<string, TTarget> BuildFromJson<TTarget>(
            this JsonPipelineBuilder<string, TTarget> builder)
        {
            return builder
                .AddTransform(_ => new JsonParseTransform())
                .AddTransform(_ => new JsonValidateTransform())
                .AddTransform(_ => new JsonDeserializeTransform<TTarget>());
        }

        public static async Task<TTarget> ProcessJsonFileAsync<TTarget>(
            this IPipeline<string, TTarget> pipeline, string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            return await pipeline.ExecuteAsync(json);
        }
    }

    [Obsolete("请使用 Microsoft.Extensions.DependencyInjection 的 IServiceCollection 进行 DI 注册")]
    public static class ServiceCollectionExtensions
    {
        private static readonly ConcurrentDictionary<string, object> _services = new();

        [Obsolete("请使用 IServiceCollection.AddSingleton 替代")]
        public static void AddSingleton<TService>(TService implementation) where TService : class
            => _services[typeof(TService).FullName!] = implementation;

        [Obsolete("请使用 IServiceCollection.AddTransient 替代")]
        public static void AddTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService, new()
            => _services[typeof(TService).FullName!] = typeof(TImplementation);

        [Obsolete("请使用 IServiceProvider.GetService 替代")]
        public static TService? GetService<TService>() where TService : class
        {
            if (_services.TryGetValue(typeof(TService).FullName!, out var service))
            {
                if (service is Type type) return Activator.CreateInstance(type) as TService;
                return service as TService;
            }
            return null;
        }

        [Obsolete("请通过 DI 容器获取 IJsonSerializer 实例")]
        public static IJsonSerializer GetSerializer()
            => GetService<IJsonSerializer>() ?? (IJsonSerializer)new UnifiedJsonSerializer();

        [Obsolete("请通过 DI 容器获取 IJsonPathEngine 实例")]
        public static IJsonPathEngine GetPathEngine()
            => GetService<IJsonPathEngine>() ?? (IJsonPathEngine)new Navigation.JsonPathEngine();

        [Obsolete("请通过 DI 容器获取 JsonSerializerOptions")]
        public static JsonSerializerOptions GetUIConfigOptions()
            => JP.Serialization.JsonSerializerFactory.Contexts.Default;

        [Obsolete("请通过 DI 容器获取 JsonPreprocessor")]
        public static JP.Utilities.JsonPreprocessor GetPreprocessor()
            => UnifiedJsonSerializer.Preprocessor;


        [Obsolete("请使用标准 IServiceProvider")]
        internal static IServiceProvider CreateProvider()
        {
            return new SimpleServiceProvider(_services);
        }

        [Obsolete("请使用标准 IServiceProvider 实现")]
        private class SimpleServiceProvider : IServiceProvider
        {
            private readonly ConcurrentDictionary<string, object> _services;
            public SimpleServiceProvider(ConcurrentDictionary<string, object> services) => _services = services;

            public object? GetService(Type serviceType)
            {
                if (_services.TryGetValue(serviceType.FullName!, out var service))
                {
                    if (service is Type type) return Activator.CreateInstance(type);
                    return service;
                }
                return null;
            }
        }
    }
}


