#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Pipeline;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Serialization;
using CommandFramework.Parsing;
using CommandFramework.Models;

namespace UniversalMiddleware.Core
{
    public static class JsonPipeline
    {
        public static IPipeline<TInput, TOutput> Create<TInput, TOutput>(
            string name = "JsonPipeline",
            JsonPipelineConfig? config = null)
        {
            return new DelegatePipeline<TInput, TOutput>(name,
                sync: input => throw new NotSupportedException("Use ProcessAsync for async JSON pipeline"),
                async: async input =>
                {
                    return (TOutput)(object)input!;
                });
        }

        public static IPipeline<TInput, TOutput> ToPipeline<TInput, TOutput>(
            this IJsonTransform<TInput, TOutput> transform,
            JsonPipelineConfig? config = null)
        {
            var inner = new DelegatePipeline<(TInput, PipelineContext), TOutput>(transform.Name,
                async: async input =>
                {
                    var (value, ctx) = input;
                    var jsonCtx = new JsonPipelineContext(ctx.CancellationToken, ctx.ServiceProvider)
                    {
                        Config = config ?? new JsonPipelineConfig()
                    };
                    foreach (var kvp in ctx.Items)
                        jsonCtx.SetItem(kvp.Key, kvp.Value);
                    return await transform.ProcessAsync(value, jsonCtx);
                });
            return inner.WithContext<TInput, TOutput>();
        }

    }

    public class JsonPipelineBuilder<TInput, TOutput>
    {
        private readonly string _name;
        private readonly List<Func<JsonPipelineConfig?, IJsonTransform>> _transformFactories = new();
        private JsonPipelineConfig _config;

        public JsonPipelineBuilder(string name = "JsonPipeline")
        {
            _name = name;
            _config = new JsonPipelineConfig { Name = name };
        }

        public JsonPipelineBuilder<TInput, TOutput> AddTransform(IJsonTransform transform)
        {
            _transformFactories.Add(_ => transform);
            return this;
        }

        public JsonPipelineBuilder<TInput, TOutput> AddTransform(Func<JsonPipelineConfig?, IJsonTransform> factory)
        {
            _transformFactories.Add(factory);
            return this;
        }

        public JsonPipelineBuilder<TInput, TOutput> Configure(Action<JsonPipelineConfig> configure)
        {
            configure(_config);
            return this;
        }

        public IPipeline<TInput, TOutput> Build()
        {
            if (_transformFactories.Count == 0)
            {
                return JsonPipeline.Create<TInput, TOutput>(_name, _config);
            }

            var buildConfig = new JsonPipelineConfig
            {
                Name = _config.Name,
                ThrowOnError = _config.ThrowOnError,
                EnableCaching = _config.EnableCaching,
                CacheExpiration = _config.CacheExpiration,
                StageConfigs = new Dictionary<string, object>(_config.StageConfigs)
            };

            var transforms = _transformFactories.Select(f => f(buildConfig)).ToList();

            var inner = new DelegatePipeline<(TInput, PipelineContext), TOutput>(_name,
                async: async input =>
                {
                    var (value, ctx) = input;
                    object? current = value;

                    var jsonCtx = new JsonPipelineContext(ctx.CancellationToken, ctx.ServiceProvider)
                    {
                        Config = buildConfig
                    };
                    foreach (var kvp in ctx.Items)
                        jsonCtx.SetItem(kvp.Key, kvp.Value);

                    foreach (var transform in transforms)
                    {
                        current = await transform.ProcessAsyncUntyped(current, jsonCtx);
                    }

                    if (current is not TOutput && current != null)
                    {
                        var transformChain = string.Join(" → ", transforms.Select(t => t.Name));
                        throw new JsonPipelineException(
                            $"Pipeline '{_name}' type mismatch: transform chain produced '{current.GetType().Name}', " +
                            $"but pipeline expects '{typeof(TOutput).Name}'. " +
                            $"Transform chain: {transformChain}");
                    }

                    return (TOutput)current!;

                });
            return inner.WithContext<TInput, TOutput>();
        }

    }


    public class JsonPipelineException : Exception
    {
        public JsonPipelineException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public class JsonParseTransform : TransformBase<string, JsonNode?>
    {
        public override string Name => "JsonParse";
        public override Task<JsonNode?> ProcessAsync(string input, JsonPipelineContext context)
        {
            try
            {
                return Task.FromResult(JsonNode.Parse(input));
            }
            catch (JsonException ex)
            {
                context.SetItem("ParseError", ex.Message);
                return Task.FromResult<JsonNode?>(null);
            }
        }
    }

    public class JsonValidateTransform : TransformBase<JsonNode?, JsonNode?>
    {
        public override string Name => "JsonValidate";
        public override Task<JsonNode?> ProcessAsync(JsonNode? input, JsonPipelineContext context)
        {
            if (input == null)
                throw new InvalidDataException("JSON input is null after parsing");
            return Task.FromResult<JsonNode?>(input);
        }
    }

    public class JsonDeserializeTransform<T> : TransformBase<JsonNode?, T?>
    {
        private static readonly Lazy<IJsonSerializer> _defaultSerializer = new(() => new UnifiedJsonSerializer());
        private readonly IJsonSerializer _serializer;

        public override string Name => $"JsonDeserialize<{typeof(T).Name}>";

        public JsonDeserializeTransform(IJsonSerializer? serializer = null)
        {
            _serializer = serializer ?? _defaultSerializer.Value;
        }

        public override Task<T?> ProcessAsync(JsonNode? input, JsonPipelineContext context)
        {
            if (input == null)
                return Task.FromResult(default(T));

            var json = input.ToJsonString();
            var result = _serializer.Deserialize<T>(json);
            return Task.FromResult(result);
        }
    }

    public class JsonFileLoadTransform : TransformBase<string, string>
    {
        public override string Name => "FileLoad";
        public override async Task<string> ProcessAsync(string input, JsonPipelineContext context)
        {
            if (!File.Exists(input))
                throw new FileNotFoundException($"JSON file not found: {input}", input);

            context.SetItem("FilePath", input);
            return await File.ReadAllTextAsync(input, context.CancellationToken);
        }
    }

    public class JsonFileSaveTransform : TransformBase<(string Path, string Content), bool>
    {
        public override string Name => "FileSave";
        public override async Task<bool> ProcessAsync((string Path, string Content) input, JsonPipelineContext context)
        {
            var dir = Path.GetDirectoryName(input.Path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(input.Path, input.Content, context.CancellationToken);
            return true;
        }
    }

}

namespace UniversalMiddleware.Middleware
{
    using CommandFramework.Parsing;
    using CommandFramework.Models;

    public class MiddlewarePipeline
    {
        private readonly List<Func<JsonCommand, JsonMiddlewareDelegate, Task<JsonCommandResult>>> _middlewares = new();
        private JsonMiddlewareDelegate? _coreHandler;

        public MiddlewarePipeline Use(Func<JsonCommand, JsonMiddlewareDelegate, Task<JsonCommandResult>> middleware)
        {
            _middlewares.Add(middleware);
            return this;
        }

        public MiddlewarePipeline Use(Func<JsonCommand, Func<Func<Task<JsonCommandResult>>, Task<JsonCommandResult>>> middleware)
        {
            _middlewares.Add(async (cmd, next) =>
            {
                var nextFunc = new Func<Task<JsonCommandResult>>(() => next(cmd));
                var middlewareFunc = middleware(cmd);
                var result = await middlewareFunc(nextFunc);
                return result;
            });
            return this;
        }

        public void SetCoreHandler(JsonMiddlewareDelegate handler)
        {
            _coreHandler = handler;
        }

        public Task<JsonCommandResult> ExecuteAsync(JsonCommand command)
        {
            if (_coreHandler == null)
                return Task.FromResult(JsonCommandResult.Fail(JsonErrorCode.AdapterNotFound, "No core handler registered"));

            JsonMiddlewareDelegate pipeline = _coreHandler;

            for (int i = _middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = _middlewares[i];
                var next = pipeline;
                pipeline = cmd => middleware(cmd, next);
            }

            return pipeline(command);
        }

        public async Task<JsonCommandResult> ExecuteTextAsync(string commandText)
        {
            var parser = new CommandParser();
            var command = parser.ParseAsJsonCommand(commandText);
            return await ExecuteAsync(command);
        }
    }
}
