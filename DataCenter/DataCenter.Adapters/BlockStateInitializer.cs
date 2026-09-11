using BasicMethod.Block;
using DataCenter.Abstractions.Lifecycle;
using Operation.StateBlock.Core;
using TConfig.ContextFrame.Abstractions;
using CoreStateBlock = Share.StateManagement.StateBlock;

namespace DataCenter.Adapters;

/// <summary>
/// 状态中心初始化块 —— 创建 StateCenter（引擎存储后端经端口适配器接入）+ 注册到宿主服务定位器。
/// 原 <c>Operation.StateBlock.Blocks.StateCenterBlock</c>（曾在 DataCenter.Core）迁出：Block 与服务定位器属宿主设施（G2）。
/// 同时实现端口 <see cref="IStateInitializer"/>，使 Core 侧编排可经端口调用而不依赖 Block 框架。
/// 只依赖通用 <see cref="StateStore"/>，不含产品侧服务定位器端口（Registry/Executor/Pipeline 由产品侧派生存储承载）。
/// </summary>
public class BlockStateInitializer : CoreStateBlock, IStateInitializer
{
    private readonly StateStore _store;
    private readonly Func<IStorageCell, StateCenter>? _centerFactory;

    public BlockStateInitializer(
        StateStore store,
        Func<IStorageCell, StateCenter>? centerFactory = null)
        : base("StateCenterBlock", store, "创建 StateCenter 并注册到 AppServiceProvider")
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _centerFactory = centerFactory;
    }

    /// <summary>本次初始化创建的状态中心（未执行/失败时为 null）。</summary>
    public StateCenter? Center { get; private set; }

    public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
    {
        var (ok, message, error) = InitializeCore();
        return Task.FromResult(ok ? BlockResult.Ok(message) : BlockResult.Fail(message, error));
    }

    public Task<InitializationOutcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var (ok, message, _) = InitializeCore();
        return Task.FromResult(ok ? InitializationOutcome.Success(message) : InitializationOutcome.Failure(message));
    }

    private (bool Ok, string Message, Exception? Error) InitializeCore()
    {
        try
        {
            var center = _centerFactory?.Invoke(_store.Cell) ?? new StateCenter(_store.Cell);
            Center = center;
            AppServiceRegistration.Register(center);
            return (true, "StateCenter 创建并注册完成", null);
        }
        catch (Exception ex)
        {
            return (false, $"StateCenter 创建失败: {ex.Message}", ex);
        }
    }
}
