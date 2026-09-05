
using BasicMethod.Block;
using Operation.StateBlock.Abstractions;
using TConfig;
using ExecutionFramework.Engine;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    public class RegistryStateBlock : CoreStateBlock
    {
        private readonly Func<ITrConfigRegistry>? _registryFactory;
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;

        public RegistryStateBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Func<ITrConfigRegistry>? registryFactory = null)
            : base("RegistryStateBlock", store, "创建和初始化共享注册表")
        {
            _opStore = store;
            _registryFactory = registryFactory;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_opStore.Registry != null)
                {
                    var existingHash = _opStore.Registry.GetHashCode();
                    return BlockResult.Ok($"注册表已存在（Hash={existingHash:X8}），跳过创建");
                }

                ITrConfigRegistry registry;
                if (_registryFactory != null)
                {
                    registry = _registryFactory();
                }
                else
                {
                    registry = new TrConfigRegistry();
                }

                _opStore.Registry = registry;

                var hash = registry.GetHashCode();
                return BlockResult.Ok($"注册表创建成功（Hash={hash:X8}）");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"注册表创建失败: {ex.Message}", ex);
            }
        }
    }
}
