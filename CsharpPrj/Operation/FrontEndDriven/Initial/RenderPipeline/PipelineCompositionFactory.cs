
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;
using ECS.Core.Lifecycle;
using ECS.Bridge;
using Modules.InternalStorage;
using Modules.InternalStorage.Core;
using UniversalMiddleware.Container;
using UniversalMiddleware.Conversion;
using UniversalMiddleware.Conversion.Components;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Implementation;
using FrontEndDriven.Diagnostics;
using TConfig;
using TConfig.Abstractions;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS.Components;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.ECS;
using ECS.Components;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.ECS.Systems;
using FrontEndDriven.ECS.Creation;
using FrontEndDriven.ControlInteraction.State;
using FrontEndDriven.ControlInteraction.EventBinding;
using FrontEndDriven.WinForms;
using FrontEndDriven.Abstractions;
using FrontEndDriven.WinForms.Applier;
using FrontEndDriven.WinForms.Adapter;
using CSG.Unified;
using Ports;
using FrameworkAbstractions.WinFormsAbstractions;

namespace FrontEndDriven.Pipeline;

public static class PipelineCompositionFactory
{
    public static RenderPipelineComposition Create(
        IECSStorage ecsStorage,
        IStorageRegistry storageRegistry,
        IControlAdapter? adapter = null,
        IStorageCell? cell = null)
    {
        if (ecsStorage == null) throw new ArgumentNullException(nameof(ecsStorage));
        if (storageRegistry == null) throw new ArgumentNullException(nameof(storageRegistry));

        adapter ??= AppServiceProvider.Resolve<IControlAdapter>()
            ?? throw new InvalidOperationException(
                "IControlAdapter 未注册到 AppServiceProvider。请确保 ControlServiceCollection.RegisterAll() 已执行。");

        var world = new EcsWorld(ecsStorage, storageRegistry);
        var entityManager = world.EntityManager;
        var storageCell = cell ?? new StorageCell($"RenderPipelineComposition-{Guid.NewGuid():N}");

        // M6·ECS 唯一真源统一：将渲染 EntityManager 覆盖注册到 AppServiceProvider。
        // 命令侧（CommandInputPort/Coordinator）经 Resolve 拿到渲染世界 → 命令实体与渲染实体同世界、
        // ID 统一分配。此前命令世界（FrameworkInitializer 注册）与渲染世界共享 ecsStorage 却独立分配 ID，
        // 渲染侧销毁/重建实体按 ID 误伤命令实体（脚本末尾命令滞留 Pending 根因）。
        AppServiceProvider.Register<EntityManager>(entityManager);

        // 阶段1+3：Entity 生命周期治理设施（Kernel 层统一）——总线（scope/实体失效广播）+ 桥（按名解析当前代）。
        // 装配顺序：先建设施再建订阅者，最后注册 Provider（订阅者构造时可 Resolve 到总线）。
        var lifecycleBus = new EntityLifecycleBus(entityManager);
        var lifetimeBridge = new EntityLifetimeBridge(entityManager);
        lifecycleBus.Register(lifetimeBridge);

        var controlPool = new ControlPool(entityManager, storageCell, storageRegistry, lifetimeBridge);
        lifecycleBus.Register(controlPool);

        // M6·物理投影校验面：ControlPool 注册到 AppServiceProvider，供命令/诊断侧（LayoutInspectSystem）做「ECS 布局 ≠ 物理 Bounds」校验。
        AppServiceProvider.Register<ControlPool>(controlPool);

        AppServiceProvider.Register<EntityLifecycleBus>(lifecycleBus);
        AppServiceProvider.Register<EntityLifetimeBridge>(lifetimeBridge);

        var controlFactory = new ControlFactory(entityManager);

        ControlStrategyRegistry.Initialize();

        var configDrivenFactory = new ConfigDrivenControlFactory(entityManager, controlFactory);

        var cmdFinder = AppServiceProvider.Resolve<ControlRegistryAwareFinder>();
        if (cmdFinder != null)
            cmdFinder.SetEntityTreeService(entityManager, controlPool);

        var csg = AppServiceProvider.Resolve<CsgFacade>() ?? new CsgFacade();

        var entityIdCache = FrontEndDriven.Shared.Utils.EntityIdCache.Instance;
        var winFormsAdapter = new WinFormsUIAdapter(controlPool, adapter);

        var ecsInputPort = new EcsInputPort(entityManager, controlPool, configDrivenFactory);

        // M6·查找与诊断收编：实体查询服务（按 scope/name/type），命令/诊断/布局系统经此查询 ECS 真源。
        // 先于 CsgEntryPoint/EcsStateAccessor/EcsEventBus 创建——按名查实体与控件枚举统一收敛到本服务（D1/D2）。
        var entityQueryService = new EntityQueryService(entityManager);
        AppServiceProvider.Register<EntityQueryService>(entityQueryService);

        var csgEntryPoint = new CsgEntryPoint(entityManager, entityQueryService);

        var splitterPanelRegistry = new SplitterPanelRegistry();

        var valueAccessor = AppServiceProvider.Resolve<TConfig.Controls.IControlValueAccessor>()
            ?? throw new InvalidOperationException(
                "IControlValueAccessor 未注册到 AppServiceProvider。请确保 ControlServiceCollection.RegisterAll() 已执行。");

        // P2·值语义契约服务（IControlService 默认实现）：EcsStateAccessor/InputCapturePort 按契约读写/订阅，
        // 删除控件类型特判（ListView 匹配选中 + 回读 Tag 规范值由 ValueContractRegistry 承载）。
        // D5 收敛：物理读写内核（IControlValueAccessor）注入为契约未覆盖类型的写值兜底——单一物理内核。
        var valueService = new WinFormsValueContract.ControlValueBridge(valueAccessor);

        var winFormsOutputPort = new WinFormsOutputPort(
            winFormsAdapter,
            entityManager, controlPool, adapter, splitterPanelRegistry, entityIdCache);

        // M3·事件与命令收编：ECS 状态访问器 + 事件总线 + 输入采集端口（装配后注册 Provider 供命令/采集懒解析）
        // D5 收敛：EcsStateAccessor 仅依赖单一 IControlService（物理内核兜底在 bridge 内），删除 IControlValueAccessor 双依赖
        var ecsStateAccessor = new EcsStateAccessor(entityManager, controlPool, valueService, entityQueryService);
        // F4.2：Failed 作用域提供者（懒解析 IStateCenter，装配期可后于 StateCenter 注册；未注册 → 空集 → 走既有日志）
        var ecsEventBus = new EcsEventBus(entityManager, ecsStateAccessor, controlPool, entityQueryService,
            failedScopeProvider: () =>
            {
                var center = AppServiceProvider.Resolve<Share.StateManagement.IStateCenter>();
                if (center == null) return System.Array.Empty<string>();
                return center.Scopes.GetScopes()
                    .Where(s => center.Scopes.GetState(s)?.State == Share.StateManagement.ScopeState.Failed)
                    .ToArray();
            });
        // R2·收缩：InputCapturePort 只做值变更回灌 ECS（不再采集 click/doubleclick→函数，事件→函数由物理绑定链单通道）
        var inputCapturePort = new InputCapturePort(ecsStateAccessor, valueService);
        AppServiceProvider.Register<IEcsStateAccessor>(ecsStateAccessor);
        AppServiceProvider.Register<IInteractionEventPort>(ecsEventBus);
        AppServiceProvider.Register<TConfig.Controls.IControlService>(valueService);
        AppServiceProvider.Register<FrontEndDriven.ControlInteraction.EventBinding.EcsEventBus>(ecsEventBus);
        AppServiceProvider.Register<FrontEndDriven.ControlInteraction.EventBinding.InputCapturePort>(inputCapturePort);
        AppServiceProvider.Register<FrontEndDriven.ControlInteraction.EventBinding.InputCapturePort>(inputCapturePort);
        // Phase A·作用域感知寻址：活动作用域上下文（脚本 load 设置，命令 get/set/click 解析据此优先在活动 scope 内查找）
        AppServiceProvider.Register<IScopeContext>(new FrontEndDriven.ControlLookup.ScopeContext());

        var layoutSystem = new LayoutSystem(entityManager,
            new ContainerBasisResolver(entityManager, controlPool));
        var hostBasisSystem = new HostBasisSystem(entityManager);
        var styleSystem = new StyleSystem(entityManager);
        var splitContainerSystem = new SplitContainerSystem(entityManager, controlPool, winFormsOutputPort);
        var capabilityApplier = new EcsCapabilityApplier(entityManager, controlPool, winFormsOutputPort);
        var eventBindingSystem = new EventBindingSystem(entityManager, controlPool, ecsEventBus);

        var matrixEngine = new ExecutionCenter.Matrix.MatrixEngine(world);


        var composition = new RenderPipelineComposition(
            inputPort: ecsInputPort,
            collector: csgEntryPoint,
            outputPort: winFormsOutputPort,
            controlPool: controlPool,
            entityManager: entityManager,
            capabilityApplier: capabilityApplier,
            layoutSystem: layoutSystem,
            hostBasisSystem: hostBasisSystem,
            splitContainerSystem: splitContainerSystem,
            styleSystem: styleSystem,
            eventBindingSystem: eventBindingSystem);

        composition.MatrixEngine = matrixEngine;

        // M5·单入口：渲染请求队列（scope+version 去重），唯一渲染入口
        // M3·双 Kind 化：FullLoad=StateCenter.Scopes.LoadAsync 完整事务（延迟解析 StateCenter——装配期晚于 composition）；
        // IncrementalRender=ExecuteRenderStage 渲染区间。消费经 IUIDispatcher 回投 UI 线程（D6 异步化）。
        var renderRequestQueue = new RenderRequestQueue(
            dispatcherResolver: () => AppServiceProvider.Resolve<TConfig.Abstractions.IUIDispatcher>(),
            fullLoad: async (scope, configPath) =>
            {
                var center = AppServiceProvider.Resolve<Share.StateManagement.IStateCenter>();
                if (center == null)
                {
                    FrontEndDrivenLog.Log("RenderRequestQueue_fullLoad_no_statecenter",
                        new { scope, configPath });
                    return false;
                }

                var result = await center.Scopes.LoadAsync(scope, configPath ?? string.Empty).ConfigureAwait(true);
                return result.Succeeded;
            },
            incremental: scope => composition.ExecuteRenderStage(string.IsNullOrEmpty(scope) ? null : scope));
        AppServiceProvider.Register<RenderRequestQueue>(renderRequestQueue);

        // Phase 1·反应式布局子系统：宿主容器尺寸变化 → 布局重算 + 仅 Bounds 差异投影（独立于内容管线）。
        // LayoutProjector = 布局重排唯一物理写入出口（不碰结构/属性/状态，不污染内容差异快照）。
        // 惰性解析 IUIDispatcher/HostMountService（装配期晚于 composition），消费经 LayoutScheduler 在 UI 线程单消费者执行。
        var layoutProjector = new LayoutProjector(entityManager, controlPool, adapter);
        var layoutSubsystem = LayoutSubsystem.Create(
            dispatcherResolver: () => AppServiceProvider.Resolve<TConfig.Abstractions.IUIDispatcher>(),
            hostMountResolver: () => AppServiceProvider.Resolve<FrontEndDriven.Container.HostMountService>(),
            hostBasisSystem: hostBasisSystem,
            layoutSystem: layoutSystem,
            projector: layoutProjector);
        AppServiceProvider.Register(layoutSubsystem);

        // Tab 内容重排触发系统（TabControl.Resize → 该 scope reflow）：
        // LayoutSubsystem.RegisterScope 与 Audit 兜底经 AppServiceProvider 惰性解析。
        AppServiceProvider.Register(new FrontEndDriven.ECS.Systems.TabContentReflowSystem(entityManager, controlPool));

        // ★ 2026-08-11 验证对齐接线：应用级共享校验服务（对齐 ValidationFramework 的实体审计），
        // RenderValidationStage 惰性解析，测试/审计也可经 AppServiceProvider 复用同一引擎。
        AppServiceProvider.Register(new FrontEndDriven.Validation.ValidationService());

        return composition;
    }
}