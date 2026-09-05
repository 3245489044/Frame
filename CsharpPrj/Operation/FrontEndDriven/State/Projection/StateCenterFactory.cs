using FrontEndDriven.Container;
using FrontEndDriven.Eventing;
using FrontEndDriven.Gate;
using Operation.StateBlock.Core;
using Share.StateManagement;
using TConfig;
using TConfig.Abstractions;

namespace FrontEndDriven.State.Projection;

/// <summary>
/// StateCenter 工厂（幂等单例）：创建 StateCenter（IStorageCell 后端）+ 注册三个生命周期 Provider。
/// 消除每窗体重复创建（U15）；EcsGate 的挂载/销毁出口同步注入（R5/R3）。
/// </summary>
public static class StateCenterFactory
{
    private static readonly object _lock = new();
    private static StateCenter? _instance;

    public static StateCenter GetOrCreate(
        TConfig.ContextFrame.Abstractions.IStorageCell cell,
        EcsGate gate,
        IUIDispatcher uiDispatcher,
        Func<string, Task<UIConfiguration?>> configLoader,
        HostMountService hostMount,
        EventPlatform platform)
    {
        if (_instance != null) return _instance;

        lock (_lock)
        {
            if (_instance != null) return _instance;

            _instance = Create(
                cell, gate, uiDispatcher, configLoader, hostMount, platform);
            return _instance;
        }
    }

    /// <summary>幂等：同进程仅创建一个 StateCenter（应用级，与窗体无关）。</summary>
    public static void Reset() => _instance = null;

    private static StateCenter Create(
        TConfig.ContextFrame.Abstractions.IStorageCell cell,
        EcsGate gate,
        IUIDispatcher uiDispatcher,
        Func<string, Task<UIConfiguration?>> configLoader,
        HostMountService hostMount,
        EventPlatform platform)
    {
        var center = new StateCenter(cell);

        var composition = new CompositionLifecycleAdapter(gate.Composition);
        // 阶段4：HostMountService 直接实现 IContainerLifecycle——容器生命周期 Provider 收敛到宿主服务单一实现，
        // ContainerLifecycleAdapter 已删除（寻址/挂载/计数/状态/注册全经 HostMountService）。
        var scopes = new ScopeLifecycleAdapter(
            gate, uiDispatcher, configLoader, composition, hostMount, hostMount, platform);

        center.RegisterCompositionLifecycle(composition);
        center.RegisterContainerLifecycle(hostMount);
        center.RegisterScopeLifecycle(scopes);

        // EcsGate 出口收敛（R3/M6）：scope 销毁走 ICompositionLifecycle.ScopeDestroyed（绝不 Dispose）；
        // 挂载状态写入已从 EcsGate 移除（MarkMounted 单写 = ScopeLifecycleAdapter.RenderAsync ⑥），
        // SetContainerLifecycle 已删除（M6 收敛）。
        gate.SetCompositionLifecycle(composition);

        return center;
    }
}
