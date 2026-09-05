using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Container;
using FrontEndDriven.Implementation;
using FrontEndDriven.Services;
using FrontEndDriven.WinForms.Adapter;
using TConfig.Abstractions;
using TConfig.ContextFrame.Abstractions;
using TConfig.Controls;

namespace FrontEndDriven.Registration;

public static class ControlServiceCollection
{
    private static volatile bool _registered;
    private static readonly object _lock = new();

    private static void RegisterService<T>(IStorageRegistry registry, string key, T service)
    {
        registry.Register(key, service);
        AppServiceProvider.Register<T>(service);
    }

    public static void RegisterAll(IStorageRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (_registered) return;
        lock (_lock)
        {
            if (_registered) return;

            // 普通控件索引（容器角色控件统一由 State 层 ContainerRegistry 登记，见 StateServiceCollection）。
            // 容器聚拢点重构：原 ContainerRegistry[ControlIndex] 更名为 ControlIndexRegistry，仅承载非容器角色控件。
            var ctrlRegistry = new ControlIndexRegistry();

            var cmdFinder = new ControlRegistryAwareFinder(ctrlRegistry);
            AppServiceProvider.Register<ControlRegistryAwareFinder>(cmdFinder);
            registry.Register("FrontEndDriven.IControlFinder", cmdFinder);

            var valueWriter = new CompositeValueWriter();
            valueWriter.AddWriter(new WinFormsControlValueWriter());
            RegisterService<TConfig.Controls.IControlValueAccessor>(
                registry, "FrontEndDriven.IControlValueAccessor", valueWriter);

            RegisterService<IControlRegistry>(
                registry, "FrontEndDriven.IControlRegistry", ctrlRegistry);

            RegisterService<IControlAdapter>(
                registry, "FrontEndDriven.IControlAdapter", new WinFormsControlAdapter());

            var uiDispatcher = new WinFormsUIDispatcher();
            registry.Register("FrontEndDriven.IUIDispatcher", uiDispatcher);
            AppServiceProvider.Register<IUIDispatcher>(uiDispatcher);

            // IControlFinder 单实现收敛（SingleImplementation）：官方单一面复用 ControlRegistryAwareFinder
            // （多级查找聚合器，ControlPool→EntityTree→Registry→TreeWalk 深搜兜底），删除 ControlFinderAdapter
            // 纯深搜实现——其深搜兜底能力已作为末位源（TreeWalkSource）注册进查找链，行为语义不丢失。
            RegisterService<IControlFinder>(
                registry, "FrontEndDriven.WinFormsAbstractions.IControlFinder", cmdFinder);
            RegisterService<IUiThreadDispatcher>(
                registry, "FrontEndDriven.IUiThreadDispatcher", new UiThreadDispatcherAdapter());

            _registered = true;
        }
    }
}