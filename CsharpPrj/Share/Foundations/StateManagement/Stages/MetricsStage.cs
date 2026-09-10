
using Share.StateManagement;
using Modules.StateManagement;

namespace Share.StateManagement.Stages
{
    public class MetricsStage : StageBase
    {
        private readonly IHandlerRegistry<string, IMetricsCollector> _collectorRegistry;

        public MetricsStage(
            IHandlerRegistry<string, IMetricsCollector>? collectorRegistry = null,
            string name = "Metrics",
            string? description = null)
            : base(name, description ?? "通用指标收集 - 基于 IHandlerRegistry")
        {
            _collectorRegistry = collectorRegistry ?? CreateDefaultRegistry();
        }

        private static IHandlerRegistry<string, IMetricsCollector> CreateDefaultRegistry()
        {
            var registry = new HandlerRegistry<string, IMetricsCollector>();
            registry.Register("duration", new DurationMetricsCollector());
            registry.Register("memory", new MemoryMetricsCollector());
            return registry;
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            foreach (var kvp in _collectorRegistry.GetAll())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await kvp.Value.CollectAsync(context, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogWarn(context, $"指标收集器 {kvp.Key} 执行失败: {ex.Message}");
                }
            }

            LogInfo(context, $"指标收集完成: {_collectorRegistry.Count} 个收集器");
        }

        public void RegisterCollector(string name, IMetricsCollector collector)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名称不能为空", nameof(name));
            if (collector == null)
                throw new ArgumentNullException(nameof(collector));
            _collectorRegistry.Register(name, collector);
        }

        public bool RemoveCollector(string name)
        {
            return _collectorRegistry.Remove(name);
        }


        private class DurationMetricsCollector : IMetricsCollector
        {
            public string Name => "duration";

            public Task CollectAsync(IStageContext context, CancellationToken cancellationToken)
            {
                var startTime = context.Data.Get<DateTime?>("ExecutionStartTime");
                if (startTime.HasValue)
                {
                    var duration = DateTime.UtcNow - startTime.Value;
                    context.Data.Set("ExecutionDuration", duration);
                }

                return Task.CompletedTask;
            }
        }

        private class MemoryMetricsCollector : IMetricsCollector
        {
            public string Name => "memory";

            public Task CollectAsync(IStageContext context, CancellationToken cancellationToken)
            {
                var memory = GC.GetTotalMemory(false);
                context.Data.Set("MemoryUsageBytes", memory);
                return Task.CompletedTask;
            }
        }
    }
}
