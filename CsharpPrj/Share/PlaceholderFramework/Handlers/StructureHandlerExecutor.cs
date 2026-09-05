
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;
using MetaCommand;
using BasicPipeline = Modules.Pipeline;
using TConfig;

namespace Common.PlaceholderFramework.Handlers
{
    public class StructureHandlerExecutor : BaseCommandExecutor
    {
        private readonly Core.IStructureHandler _handler;

        public StructureHandlerExecutor(Core.IStructureHandler handler, string? executorId = null)
            : base(
                executorId ?? handler.Name ?? "StructureHandler",
                $"StructureHandler:{handler.Name}",
                "1.0")
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override async Task<OperationResult<object>> DoAsync(
            string action, string? target,
            Dictionary<string, object>? parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.Equals(action, "process", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<object>.FailureResult(
                        $"Unsupported action: '{action}'. Supported: 'process'",
                        ErrorCode.ActionNotFound);

                if (target == null)
                    return OperationResult<object>.FailureResult(
                        "Target (document) is required for 'process' action",
                        ErrorCode.InvalidParameter);

                var errors = new TConfig.ErrorCollector();
                var context = new BasicPipeline.PipelineContext();

                if (parameters != null && parameters.TryGetValue("sourceFile", out var sourceFile) && sourceFile is string sf)
                    context.SourceFile = sf;

                if (!_handler.CanHandle(target))
                    return OperationResult<object>.SuccessResult(
                        null,
                        $"Handler '{_handler.Name}' cannot handle the target, skipped");

                _handler.Process(target, errors, context);

                var result = new Dictionary<string, object>
                {
                    ["handlerName"] = _handler.Name,
                    ["errorCount"] = errors.Count,
                    ["hasErrors"] = errors.HasErrors,
                    ["hasFatalErrors"] = errors.HasFatalErrors,
                    ["sourceFile"] = context.SourceFile ?? string.Empty
                };

                var message = errors.Count > 0
                    ? $"Handler '{_handler.Name}' completed with {errors.Count} issues"
                    : $"Handler '{_handler.Name}' completed successfully";

                return OperationResult<object>.SuccessResult(result, message);
            }
            catch (OperationCanceledException)
            {
                return OperationResult<object>.FailureResult(
                    "Operation was cancelled", ErrorCode.OperationCanceled);
            }
            catch (Exception ex)
            {
                return OperationResult<object>.FailureResult(
                    $"Handler '{_handler.Name}' execution failed: {ex.Message}",
                    ErrorCode.ExecutionFailed, ex);
            }
        }

        public override Task<OperationResult<object>> GetAsync(
            string entity,
            Dictionary<string, object>? queryParams,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var canHandle = _handler.CanHandle(entity);
                var result = new Dictionary<string, object>
                {
                    ["handlerName"] = _handler.Name,
                    ["canHandle"] = canHandle,
                    ["target"] = entity
                };

                return Task.FromResult(
                    OperationResult<object>.SuccessResult(
                        result,
                        canHandle
                            ? $"Handler '{_handler.Name}' can handle '{entity}'"
                            : $"Handler '{_handler.Name}' cannot handle '{entity}'"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(
                    OperationResult<object>.FailureResult(
                        $"GetAsync failed: {ex.Message}", ErrorCode.ExecutionFailed, ex));
            }
        }

        public override Task<OperationResult<WorldModel>> LookAsync(
            string? scope,
            Dictionary<string, object>? options,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var model = new WorldModel
                {
                    AdapterId = ExecutorId,
                    Scope = scope ?? "default",
                    State = new Dictionary<string, object>
                    {
                        ["handlerName"] = _handler.Name,
                        ["handlerType"] = _handler.GetType().Name
                    },
                    Entities = new List<EntityDescriptor>
                    {
                        new EntityDescriptor
                        {
                            Name = _handler.Name,
                            Type = "StructureHandler",
                            Description = $"IStructureHandler wrapper for '{_handler.Name}'",
                            SupportedActions = new List<string> { "process" }
                        }
                    }
                };

                return Task.FromResult(
                    OperationResult<WorldModel>.SuccessResult(model, $"Handler '{_handler.Name}' status"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(
                    OperationResult<WorldModel>.FailureResult(
                        $"LookAsync failed: {ex.Message}", ErrorCode.ExecutionFailed, ex));
            }
        }

        public override Task<OperationResult<bool>> WaitAsync(
            string condition, int timeoutMs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                OperationResult<bool>.SuccessResult(true, "Wait is not supported by StructureHandler, skipped"));
        }

        public override Task<OperationResult<bool>> AssertAsync(
            string predicate,
            Dictionary<string, object>? parameters,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                OperationResult<bool>.SuccessResult(true, "Assert is not supported by StructureHandler, skipped"));
        }

        public override Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(
            string? scope,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var descriptors = new List<EntityDescriptor>
                {
                    new EntityDescriptor
                    {
                        Name = _handler.Name,
                        Type = "StructureHandler",
                        Description = $"IStructureHandler: {_handler.Name}",
                        Properties = new Dictionary<string, object>
                        {
                            ["handlerType"] = _handler.GetType().Name
                        },
                        SupportedActions = new List<string> { "process" }
                    }
                };

                return Task.FromResult(
                    OperationResult<List<EntityDescriptor>>.SuccessResult(
                        descriptors, $"Discovered {descriptors.Count} handler(s)"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(
                    OperationResult<List<EntityDescriptor>>.FailureResult(
                        $"DiscoverAsync failed: {ex.Message}", ErrorCode.ExecutionFailed, ex));
            }
        }
    }
}
