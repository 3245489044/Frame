
using BasicMethod.Block;
using Operation.StateBlock.Abstractions;
using TConfig;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    public class FunctionStorageStateBlock : CoreStateBlock
    {
        private readonly Action<ITrConfigRegistry>? _registerFunctions;
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;

        public FunctionStorageStateBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Action<ITrConfigRegistry>? registerFunctions = null)
            : base("FunctionStorageStateBlock", store, "注册 FunctionStorage 内部函数")
        {
            _opStore = store;
            _registerFunctions = registerFunctions;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var registry = _opStore.Registry;

                if (registry == null)
                {
                    return BlockResult.Fail("注册表未创建，请确保 RegistryStateBlock 先执行");
                }

                if (_registerFunctions != null)
                {
                    _registerFunctions(registry);
                    return BlockResult.Ok("FunctionStorage 函数注册完成");
                }

                var registryHash = registry.GetHashCode();
                return BlockResult.Ok($"FunctionStorage 状态块执行完成（RegistryHash={registryHash:X8}），等待外部注册委托");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"FunctionStorage 函数注册失败: {ex.Message}", ex);
            }
        }
    }
}
