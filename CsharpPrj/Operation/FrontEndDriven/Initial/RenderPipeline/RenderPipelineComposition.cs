
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using Modules.InternalStorage;
using Modules.InternalStorage.Core;
using UniversalMiddleware.Container;
using UniversalMiddleware.Conversion;
using UniversalMiddleware.Conversion.Components;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Implementation;
using TConfig;
using TConfig.Abstractions;
using TConfig.ContextFrame.Abstractions;
using TConfig.ECS.Components;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS;
using ECS.Components;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.ECS.Systems;
using FrontEndDriven.ECS.Creation;
using FrontEndDriven.WinForms;
using FrontEndDriven.Abstractions;
using FrontEndDriven.WinForms.Applier;
using FrontEndDriven.WinForms.Adapter;
using CSG.Unified;
using Modules.ComponentData.Extensions;
using Ports;

namespace FrontEndDriven.Pipeline;

public sealed class RenderPipelineComposition : IDisposable
{

    public EcsInputPort InputPort { get; }

    public CsgEntryPoint Collector { get; }

    public WinFormsOutputPort OutputPort { get; }


    private readonly EcsCapabilityApplier _capabilityApplier;
    private readonly LayoutSystem _layoutSystem;
    private readonly SplitContainerSystem _splitContainerSystem;
    private readonly StyleSystem _styleSystem;
    private readonly EventBindingSystem _eventBindingSystem;
    private readonly ControlPool _controlPool;
    private readonly EntityManager _entityManager;
    private readonly SplitterPanelRegistry _splitterPanelRegistry;
    private readonly RenderStageRunner _renderStageRunner;


    public ControlPool ControlPool => _controlPool;

    public EntityManager EntityManager => _entityManager;

    public ExecutionCenter.Matrix.MatrixEngine MatrixEngine { get; internal set; } = null!;

    private bool _hasExecuted;
    private bool _disposed;


    public int LastCollectedNodeCount { get; private set; }

    /// <summary>渲染是否已执行（唯一生命周期标志；由 RenderBindStage 经 ReportExecuted 置位，Clear 复位）。</summary>
    public bool HasExecuted => _hasExecuted;



    public event Action? PipelineCompleted;


    public static RenderPipelineComposition Create(
        IECSStorage ecsStorage,
        IStorageRegistry storageRegistry,
        IControlAdapter? adapter = null,
        IStorageCell? cell = null)
        => PipelineCompositionFactory.Create(ecsStorage, storageRegistry, adapter, cell);


    public RenderPipelineComposition(
        EcsInputPort inputPort,
        CsgEntryPoint collector,
        WinFormsOutputPort outputPort,
        ControlPool controlPool,
        EntityManager entityManager,
        EcsCapabilityApplier capabilityApplier,
        LayoutSystem layoutSystem,
        HostBasisSystem hostBasisSystem,
        SplitContainerSystem splitContainerSystem,
        StyleSystem styleSystem,
        EventBindingSystem eventBindingSystem)
    {
        InputPort = inputPort ?? throw new ArgumentNullException(nameof(inputPort));
        Collector = collector ?? throw new ArgumentNullException(nameof(collector));
        OutputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _capabilityApplier = capabilityApplier ?? throw new ArgumentNullException(nameof(capabilityApplier));
        _layoutSystem = layoutSystem ?? throw new ArgumentNullException(nameof(layoutSystem));
        _splitContainerSystem = splitContainerSystem ?? throw new ArgumentNullException(nameof(splitContainerSystem));
        _styleSystem = styleSystem ?? throw new ArgumentNullException(nameof(styleSystem));
        _eventBindingSystem = eventBindingSystem ?? throw new ArgumentNullException(nameof(eventBindingSystem));

        _splitterPanelRegistry = outputPort.SplitterPanelRegistry;

        // P2: RenderRunResult 单一回传（RD4）——LastCollectedNodeCount/HasExecuted 由 Execute/ExecuteRenderStage 消费 result 置位
        _renderStageRunner = new RenderStageRunner(
            entityManager,
            controlPool,
            inputPort,
            collector,
            outputPort,
            capabilityApplier,
            layoutSystem,
            hostBasisSystem,
            splitContainerSystem,
            styleSystem,
            eventBindingSystem,
            outputPort.SplitterPanelRegistry,
            () => _disposed);

    }


    public RenderRunResult Execute(
        UIConfiguration config,
        string scope = "MainLayout")
    {
        ThrowIfDisposed();

        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // C5：rootContainer 参数已删除（仅 BuildResult 日志用，渲染管线无任何 Stage 消费；宿主寻址统一经 HostMountService）
        var result = _renderStageRunner.ExecuteCore(config, scope);

        // Execute 仅执行 Load/Creation 区间（RenderExecuted=false，不渲染）；
        // 渲染计数由后续 ExecuteRenderStage 实际产出，此处不计入避免误导（collected=0 是"区间未运行"而非"结果为空"）。
        ApplyRunResult(result);

        // BuildResult 已保证非渲染区间(renderExecuted=false)下 CollectedCount/AppliedCount=0，此处不再重复判断
        FrontEndDrivenLog.Log("RenderPipelineComposition_Execute_summary",
            new
            {
                scope,
                rootEntityId = result.RootEntity.Id.ToString(),
                collectedCount = result.CollectedCount,
                appliedCount = result.AppliedCount,
                mountedCount = result.MountedCount,
                orphanCount = result.OrphanCount,
                elapsedMs = result.ElapsedMs
            });

        PipelineCompleted?.Invoke();

        return result;
    }



    public void DestroyScopeEntities(string scopeName)
    {
        ThrowIfDisposed();
        _renderStageRunner.DestroyScopeEntities(scopeName);
    }

    /// <summary>
    /// 渲染区间（S3 作用域感知）。scope 为 null/空 时全量渲染（兼容无参调用）；
    /// 显式 scope 时仅渲染该作用域（修复「Side 加载却全量重放」缺陷）。
    /// P7：返回 RenderRunResult（含挂载统计），供 EcsGate/审计判定「渲染是否挂载可见」。
    /// </summary>
    public RenderRunResult ExecuteRenderStage(string? scope = null)
    {
        ThrowIfDisposed();
        var result = _renderStageRunner.ExecuteRenderStage(scope);
        ApplyRunResult(result);
        return result;
    }

    public void Clear()
    {
        _controlPool.Clear();
        _entityManager.Clear();
        _hasExecuted = false;
        LastCollectedNodeCount = 0;
    }

    // ── P2 私有辅助：RenderRunResult 回传 → 生命周期/计数状态（RD4） ──

    private void ApplyRunResult(RenderRunResult result)
    {
        if (result.RenderExecuted)
        {
            LastCollectedNodeCount = result.CollectedCount;
            _hasExecuted = true;
        }
    }


    public void Dispose()
    {
        if (_disposed) return;

        PipelineCompleted = null;
        Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RenderPipelineComposition));
    }
}