
using Share.StateManagement;
using Modules.StateManagement;

namespace Share.StateManagement.Stages
{
    public class AdapterStage : StageBase
    {
        private readonly IHandlerRegistry<string, IWorkflowAdapter> _adapterRegistry;

        public AdapterStage(
            IHandlerRegistry<string, IWorkflowAdapter> adapterRegistry,
            string name = "Adapter",
            string? description = null)
            : base(name, description ?? "通用适配器执行 - 基于 IHandlerRegistry")
        {
            // 必填端口（T）：适配器注册表由装配点显式提供；禁 ?? new 默认冒充装配。
            _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            foreach (var kvp in _adapterRegistry.GetAll())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    LogDebug(context, $"执行适配器: {kvp.Key}");
                    kvp.Value.Adapt(context);
                    LogDebug(context, $"适配器执行完成: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    LogWarn(context, $"适配器 {kvp.Key} 执行失败: {ex.Message}");
                }
            }

            LogInfo(context, $"适配器执行完成: {_adapterRegistry.Count} 个适配器");
        }

        public void RegisterAdapter(string name, IWorkflowAdapter adapter)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名称不能为空", nameof(name));
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            _adapterRegistry.Register(name, adapter);
        }

        public void RegisterRange(IEnumerable<KeyValuePair<string, IWorkflowAdapter>> adapters)
        {
            if (adapters == null)
                throw new ArgumentNullException(nameof(adapters));

            foreach (var kvp in adapters)
            {
                RegisterAdapter(kvp.Key, kvp.Value);
            }
        }

        public void RegisterRange(IDictionary<string, IWorkflowAdapter> adapters)
        {
            if (adapters == null)
                throw new ArgumentNullException(nameof(adapters));

            foreach (var kvp in adapters)
            {
                RegisterAdapter(kvp.Key, kvp.Value);
            }
        }

        public bool RemoveAdapter(string name)
        {
            return _adapterRegistry.Remove(name);
        }
    }
}
