
using BasicMethod.Block;
using Operation.StateBlock.Abstractions;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    public class AppServiceStateBlock : CoreStateBlock
    {
        private readonly Action<Operation.StateBlock.Abstractions.IStateStore>? _configureServices;
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;

        public AppServiceStateBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Action<Operation.StateBlock.Abstractions.IStateStore>? configureServices = null)
            : base("AppServiceStateBlock", store, "初始化 DI 容器")
        {
            _opStore = store;
            _configureServices = configureServices;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_configureServices != null)
                {
                    _configureServices(_opStore);
                    return BlockResult.Ok("DI 容器配置完成");
                }

                return BlockResult.Ok("AppService 状态块执行完成，等待外部配置委托");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"DI 容器配置失败: {ex.Message}", ex);
            }
        }
    }
}
