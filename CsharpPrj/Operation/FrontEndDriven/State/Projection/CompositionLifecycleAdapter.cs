using FrontEndDriven.Pipeline;
using Share.StateManagement;

namespace FrontEndDriven.State.Projection;

/// <summary>
/// 组合生命周期 Provider（治 L1）：应用级唯一持有 composition，
/// ScopeDestroyed 只销毁该 scope 实体（绝不 Dispose），AppShutdown 是唯一释放点（R3）。
/// </summary>
public sealed class CompositionLifecycleAdapter : ICompositionLifecycle
{
    private readonly RenderPipelineComposition _composition;
    private bool _appShutdown;

    public CompositionLifecycleAdapter(RenderPipelineComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public void ScopeDestroyed(string scope)
    {
        if (_appShutdown) return;
        _composition.DestroyScopeEntities(scope); // 只销毁该 scope 实体，绝不 Dispose composition
    }

    public void AppShutdown()
    {
        if (_appShutdown) return;
        _appShutdown = true;
        _composition.Dispose(); // 进程退出唯一释放点（R3）
    }
}
