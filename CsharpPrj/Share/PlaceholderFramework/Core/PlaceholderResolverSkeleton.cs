using System.Collections.Concurrent;
using Graphkey.Core;
using SkeletonFlow;
using Common.PlaceholderFramework.Integrated.Handlers;
using Common.PlaceholderFramework.Storage;

namespace Common.PlaceholderFramework.Core
{
    public class PlaceholderResolverSkeleton : SkeletonBase<object, object>
    {
        private readonly IPlaceholderHandler[] _handlers;
        private readonly IMetadataStore _metadataStore;
        private readonly IResolutionContext _resolutionContext;
        private readonly bool _batchSave;
        private readonly object? _metadataLock;
        private readonly object _syncLock = new();

        private const string CtxKeyPreviousOutputs = "__Placeholder_PreviousOutputs";
        private const string CtxKeyInputKeys = "__Placeholder_InputKeys";
        private const string CtxKeyResolvedValues = "__Placeholder_ResolvedValues";

        public PlaceholderResolverSkeleton(
            IEnumerable<IPlaceholderHandler> handlers,
            IMetadataStore metadataStore = null,
            IResolutionContext resolutionContext = null,
            bool batchSave = false)
            : base(nameof(PlaceholderResolverSkeleton), "统一解析并分发所有 Placeholder")
        {
            _handlers = handlers?.ToArray() ?? Array.Empty<IPlaceholderHandler>();
            _metadataStore = metadataStore ?? new MemoryMetadataStore();
            _resolutionContext = resolutionContext;
            _batchSave = batchSave;
            _metadataLock = _batchSave ? new object() : null;
        }

        private void ProcessPlaceholder(
            KeyValuePair<string, Graphkey.Core.Placeholder> kvp,
            IResolutionContext resolveCtx,
            ConcurrentDictionary<string, string> resolvedValues,
            ConcurrentBag<PlaceholderMetadata> metadataCache,
            SkeletonContext ctx,
            string batchId,
            string moduleName)
        {
            var key = kvp.Key;
            var placeholder = kvp.Value;

            try
            {
                var resolvedValue = PlaceholderResolver.Resolve(placeholder, resolveCtx);

                lock (_metadataLock ?? _syncLock)
                {
                    resolvedValues[key] = resolvedValue;

                    var processedHandlers = new HashSet<string>();

                    foreach (var handler in _handlers)
                    {
                        if (!handler.CanHandle(placeholder))
                            continue;

                        try
                        {
                            handler.Handle(placeholder, resolvedValue, ctx, batchId);
                        }
                        catch (Exception ex)
                        {
                            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.resolver.handler_failed", new { handlerName = handler.Name, key, error = ex.Message });
                        }

                        if (!processedHandlers.Add(handler.Name))
                            continue;

                        try
                        {
                            var metadata = PlaceholderMetadata.FromPlaceholder(
                                placeholder, resolvedValue,
                                module: moduleName,
                                batchId: batchId,
                                handlerName: handler.Name);

                            if (_batchSave && metadataCache != null)
                            {
                                metadataCache.Add(metadata);
                            }
                            else
                            {
                                _metadataStore.Save(metadata);
                            }
                        }
                        catch (Exception ex)
                        {
                            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.resolver.metadata_save_failed", new { key, error = ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.resolver.resolve_failed", new { key, error = ex.Message });
            }
        }

        private static void WriteResolvedValuesToContext(
            ConcurrentDictionary<string, string> resolvedValues, SkeletonContext ctx)
        {
            if (resolvedValues.Count == 0)
                return;

            if (!ctx.TryGetData<Dictionary<string, string>>(out var existing, CtxKeyResolvedValues) || existing == null)
            {
                existing = new Dictionary<string, string>();
            }
            foreach (var kvp in resolvedValues)
            {
                existing[kvp.Key] = kvp.Value;
            }
            ctx.SetData(existing, CtxKeyResolvedValues);
        }

        protected override object ExecuteCore(object input, SkeletonContext ctx)
        {
            var batchId = ctx.GetBatchId();
            var moduleName = ctx.GetModuleName();

            var placeholders = ctx.GetAllPlaceholders().ToList();
            if (placeholders.Count == 0)
                return input;

            var resolveCtx = _resolutionContext ?? BuildResolutionContext(ctx);
            var metadataCache = _batchSave ? new ConcurrentBag<PlaceholderMetadata>() : null;
            var resolvedValues = new ConcurrentDictionary<string, string>();

            foreach (var kvp in placeholders)
            {
                ProcessPlaceholder(kvp, resolveCtx, resolvedValues, metadataCache, ctx, batchId, moduleName);
            }

            WriteResolvedValuesToContext(resolvedValues, ctx);

            if (_batchSave && metadataCache != null && metadataCache.Count > 0)
                _metadataStore.SaveBatch(metadataCache);

            return input;
        }

        protected override async Task<object> ExecuteCoreAsync(object input, SkeletonContext ctx)
        {
            var batchId = ctx.GetBatchId();
            var moduleName = ctx.GetModuleName();

            var placeholders = ctx.GetAllPlaceholders().ToList();
            if (placeholders.Count == 0)
                return input;

            var resolveCtx = _resolutionContext ?? BuildResolutionContext(ctx);
            var metadataCache = _batchSave ? new ConcurrentBag<PlaceholderMetadata>() : null;
            var resolvedValues = new ConcurrentDictionary<string, string>();

            var tasks = placeholders.Select(kvp => Task.Run(() =>
            {
                ProcessPlaceholder(kvp, resolveCtx, resolvedValues, metadataCache, ctx, batchId, moduleName);
            }));

            await Task.WhenAll(tasks);

            WriteResolvedValuesToContext(resolvedValues, ctx);

            if (_batchSave && metadataCache != null && metadataCache.Count > 0)
                await _metadataStore.SaveBatchAsync(metadataCache);

            return input;
        }

        private static IResolutionContext BuildResolutionContext(SkeletonContext ctx)
        {
            if (!ctx.TryGetData<List<string>>(out var previousOutputs, CtxKeyPreviousOutputs) || previousOutputs == null)
                previousOutputs = new List<string>();

            if (!ctx.TryGetData<List<string>>(out var inputKeys, CtxKeyInputKeys) || inputKeys == null)
                inputKeys = new List<string>();

            return new DefaultResolutionContext
            {
                PreviousOutputs = previousOutputs.AsReadOnly(),
                InputKeys = inputKeys.AsReadOnly()
            };
        }

        private class DefaultResolutionContext : IResolutionContext
        {
            public IReadOnlyList<string> PreviousOutputs { get; init; }
            public IReadOnlyCollection<string> InputKeys { get; init; }

            public IReadOnlyList<string> GetNodeOutputs(string nodeId)
            {
                return Array.Empty<string>();
            }

            public IReadOnlyList<string> GetNodeOutputs(int index)
            {
                return Array.Empty<string>();
            }
        }
    }
}
