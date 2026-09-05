
using BasicMethod.Block;
using Operation.StateBlock.Abstractions;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    public class FileDrivenStateBlock : CoreStateBlock
    {
        private readonly Action<Operation.StateBlock.Abstractions.IStateStore>? _configureFileDriven;
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;

        public FileDrivenStateBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Action<Operation.StateBlock.Abstractions.IStateStore>? configureFileDriven = null)
            : base("FileDrivenStateBlock", store, "注册 FileDriven 层函数")
        {
            _opStore = store;
            _configureFileDriven = configureFileDriven;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_configureFileDriven != null)
                {
                    _configureFileDriven(_opStore);
                    return BlockResult.Ok("FileDriven 配置完成");
                }

                return BlockResult.Ok("FileDriven 状态块执行完成，等待外部配置委托");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"FileDriven 配置失败: {ex.Message}", ex);
            }
        }
    }
}
