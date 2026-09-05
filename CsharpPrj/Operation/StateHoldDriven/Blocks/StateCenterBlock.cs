
using BasicMethod.Block;
using Operation.StateBlock.Abstractions;
using Operation.StateBlock.Core;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace Operation.StateBlock.Blocks
{
    /// <summary>
    /// StateCenterBlock：创建 StateCenter（IStorageCell 后端）+ 注册到 AppServiceProvider。
    /// 2026-08-05 StateCenter 收敛：追加在默认初始化管线末尾，保持初始化管线语义。
    /// </summary>
    public class StateCenterBlock : CoreStateBlock
    {
        private readonly Operation.StateBlock.Abstractions.IStateStore _opStore;
        private readonly Func<TConfig.ContextFrame.Abstractions.IStorageCell, StateCenter>? _centerFactory;

        public StateCenterBlock(
            Operation.StateBlock.Abstractions.IStateStore store,
            Func<TConfig.ContextFrame.Abstractions.IStorageCell, StateCenter>? centerFactory = null)
            : base("StateCenterBlock", store, "创建 StateCenter 并注册到 AppServiceProvider")
        {
            _opStore = store;
            _centerFactory = centerFactory;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var stateStore = _opStore as StateStore
                    ?? throw new InvalidOperationException(
                        "StateCenterBlock 需要 IStorageCell 后端的 StateStore（经 StateStore.Cell 提供统一存储）。");

                var center = _centerFactory?.Invoke(stateStore.Cell) ?? new StateCenter(stateStore.Cell);

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
