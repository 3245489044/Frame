using ECS.Bridge;
using FrameworkAbstractions.ContainerAbstractions;
using FrontEndDriven.Container;
using FrontEndDriven.Eventing;
using FrontEndDriven.Gate;
using FrontEndDriven.State.Commands;
using FrontEndDriven.State.Projection;
using FrontEndDriven.WinForms.Adapter;
using FrameworkAbstractions.ComponentRegistry;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace FrontEndDriven.State;

public static class StateServiceCollection
{
    private static volatile bool _registered;
    private static readonly object _lock = new();

    public const string ContainerRegistryKey = "FrontEndDriven.State.ContainerRegistry";
    public const string ContainerCommandExecutorKey = "FrontEndDriven.State.ContainerCommandExecutor";

    public static StateRegistration RegisterAll(
        IStorageRegistry registry,
        IStorageCell cell,
        EcsGate gate,
        ControlPool controlPool,
        TConfig.Abstractions.IUIDispatcher uiDispatcher,
        Func<string, Task<UIConfiguration?>> configLoader,
        Func<string, System.Windows.Forms.Control?>? rootHostResolver = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(controlPool);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(configLoader);

        if (_registered)
            return GetRegistration(registry);

        lock (_lock)
        {
            if (_registered)
                return GetRegistration(registry);

            // 容器状态收敛（2026-08-05）：ContainerStateStore / HostBindingStore 类已删除——
            // 容器状态经 StateCenter（ScopeLifecycle/StateKey.Container）承载；scope↔hostKey 绑定
            // 由 ContainerRegistry 内部直接落 IStorageCell 双向映射（键空间兼容旧持久化数据）。

            // 容器聚拢点：唯一事实源（S2 三源合一）。scope↔hostKey 绑定内联（原 HostBindingStore 内部化）。
            // S4：ToHandle 经 externalHostResolver（root 直绑）→ ControlPool → visibleTreeResolver（可见树兜底）三段寻址，
            // 消除「宿主名映射在 ControlPool 被覆盖 → 未找到宿主」的假 FAIL，与 validate（可见树口径）不再分裂。
            var ordinaryControlFinder =
                AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
            Func<string, System.Windows.Forms.Control?> visibleTreeResolver = hostKey =>
                ordinaryControlFinder?.FindByName<System.Windows.Forms.Control>(hostKey);
            var containerRegistry = new ContainerRegistry(cell, controlPool, rootHostResolver, visibleTreeResolver);
            registry.Register(ContainerRegistryKey, containerRegistry);
            AppServiceProvider.Register(containerRegistry);

            // 宿主聚拢服务（HostMountService）：解析/挂载/计数/状态唯一语义据点。
            // EcsGate 的 S3 物理挂载与渲染管线计数全部收敛到此，消除「解析↔挂载↔断言」宿主实例分裂。
            var hostMount = new Container.HostMountService(containerRegistry, controlPool);
            AppServiceProvider.Register(hostMount);

            // 端口化（S3）：容器访问端口 + 唯一断言权威，供 BackEndDriven 验证/断言链复用。
            // 收敛：端口委托 HostMountService，宿主解析唯一经 Mount 服务（与物理挂载同源）。
            var accessPort = new ContainerAccessPortAdapter(hostMount);
            AppServiceProvider.Register<IContainerAccessPort>(accessPort);
            var assertionService = new ContainerAssertionService(
                accessPort,
                name => ordinaryControlFinder?.FindByName<System.Windows.Forms.Control>(name));
            AppServiceProvider.Register(assertionService);

            var platform = AppServiceProvider.Resolve<EventPlatform>()
                ?? throw new InvalidOperationException("EventPlatform 未注册到 AppServiceProvider");

            // 2026-08-05 StateCenter 收敛：先创建 StateCenter（幂等单例，消除每窗体重复注册 U15）——
            // projector 构造需要 IStateCenter（订阅驱动）。
            var stateCenter = StateCenterFactory.GetOrCreate(
                cell, gate, uiDispatcher, configLoader, hostMount, platform);

            var executor = new ContainerCommandExecutor();
            registry.Register(ContainerCommandExecutorKey, executor);
            AppServiceProvider.Register(executor);

            // Phase B·P1 根因：注册唯一「加载即渲染」服务（load/load-config-show 委托），脚本 load 渲染进主内容区 scope。
            // F3：渲染目标按配置角色路由（Content 进主内容区；Shell 幂等跳过/拒绝），消灭硬编码 Content。
            var renderRouter = new FrontEndDriven.LoadRender.RenderTargetRouter(hostMount);
            AppServiceProvider.Register<FrontEndDriven.LoadRender.IRenderTargetRouter>(renderRouter);
            var configLoadRender = new FrontEndDriven.LoadRender.ConfigLoadRenderService(
                configLoader, executor, hostMount, renderRouter);
            AppServiceProvider.Register<FrameworkAbstractions.WinFormsAbstractions.IConfigLoadRenderService>(configLoadRender);

            AppServiceProvider.Register(stateCenter);
            AppServiceProvider.Register<Share.StateManagement.IStateCenter>(stateCenter);

            _registered = true;

            return new StateRegistration(containerRegistry, executor);
        }
    }

    private static StateRegistration GetRegistration(IStorageRegistry registry)
    {
        return new StateRegistration(
            registry.Resolve<ContainerRegistry>(ContainerRegistryKey)!,
            registry.Resolve<ContainerCommandExecutor>(ContainerCommandExecutorKey)!);
    }
}

public sealed record StateRegistration(
    ContainerRegistry ContainerRegistry,
    ContainerCommandExecutor ContainerCommandExecutor);
