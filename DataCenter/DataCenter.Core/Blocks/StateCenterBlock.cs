
using BasicMethod.Block;
using Operation.StateBlock.Core;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    /// <summary>
    /// StateCenterBlock：创建 StateCenter（IStorageCell 后端）+ 注册到 AppServiceProvider。
    /// 2026-08-05 StateCenter 收敛：追加在默认初始化管线末尾，保持初始化管线语义。
    /// 2026-09-12 迁入 DataCenter（通用件）：只依赖通用 <see cref="StateStore"/>，不含产品侧服务定位器端口
    /// （Registry/Executor/Pipeline 等由产品侧派生存储承载）。
    /// </summary>
    public class StateCenterBlock : CoreStateBlock
    {
        private readonly StateStore _store;
        private readonly Func<TConfig.ContextFrame.Abstractions.IStorageCell, StateCenter>? _centerFactory;

        public StateCenterBlock(
            StateStore store,
            Func<TConfig.ContextFrame.Abstractions.IStorageCell, StateCenter>? centerFactory = null)
            : base("StateCenterBlock", store, "创建 StateCenter 并注册到 AppServiceProvider")
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _centerFactory = centerFactory;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var center = _centerFactory?.Invoke(_store.Cell) ?? new StateCenter(_store.Cell);

                FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Register(center);
                FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Register<Share.StateManagement.IStateCenter>(center);

                return BlockResult.Ok("StateCenter 创建并注册完成");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"StateCenter 创建失败: {ex.Message}", ex);
            }
        }
    }
}
