#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared;
using Share.EventBindingOrchestrator.Diagnostics;
using Share.EventBindingOrchestrator.FunctionHub;
using TConfig.ECS.Components;

namespace FrontEndDriven.ECS.Systems;

/// <summary>
/// 事件绑定系统（M3·事件与命令收编 + 共存方案 R1·物理真实挂接恢复）。
/// 职责：绑定期对配置声明了事件的实体做【物理真实挂接】（控件.Click += / .DoubleClick += ...），
/// handler 转发 ECS 事件链（EcsEventBus → 参数绑定 → FunctionCallFacade），并输出 Bound/Skipped 校验报告
/// （控件未找到 / 函数未注册 / 事件不支持 → 显式 Warning 且实体置 Failed）——物理绑定作为强制校验基础，
/// 与 ECS 状态路径（脚本命令 / 值回灌 / 投影）正交共存，不再"只标记不挂接"。
/// 实体状态收敛：Pending → Bound（全部绑定挂接成功）/ Failed（存在未挂接项，Error 带原因）。
/// </summary>
public class EventBindingSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IInteractionEventPort _eventPort;
    private readonly IEventBindingLogger _logger;
    private readonly IFunctionHub? _functionHub;

    public EventBindingSystem(
        EntityManager entityManager,
        ControlPool controlPool,
        IInteractionEventPort eventPort)
    {
        _entityManager = entityManager ?? throw new System.ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new System.ArgumentNullException(nameof(controlPool));
        _eventPort = eventPort ?? throw new System.ArgumentNullException(nameof(eventPort));
        _logger = FrontEndDrivenEventBindingLogger.Default;
        _functionHub = AppServiceProvider.Resolve<IFunctionHub>();
    }

    /// <summary>绑定期物理挂接 + 校验（RenderBindStage 调用）。</summary>
    public void Update()
    {
        var pendingEntities = CollectPendingEntities();
        if (pendingEntities.Count == 0)
        {
            _logger.Debug("EventBindingSystem_no_pending_entities",
                "没有待绑定事件的 Entity（pendingEntities.Count=0）。");
            return;
        }

        int boundCount = 0;
        var skipped = new List<string>();

        foreach (var entity in pendingEntities)
        {
            var control = _controlPool.GetControl(entity) as Control;
            if (control == null || control.IsDisposed)
            {
                skipped.Add($"Entity({entity.Id}): 控件未找到或已销毁");
                MarkFailed(entity, "控件未找到或已销毁");
                continue;
            }

            var controlName = control.Name ?? string.Empty;
            var bindings = _entityManager.TryGetComponent<EventBindingsComponent>(entity, out var bc)
                ? bc.Bindings
                : null;

            if (bindings is not { Count: > 0 })
            {
                // E5：无事件声明的实体仅标记 Bound（无需挂接），不混入「已挂接绑定」统计口径
                MarkBound(entity);
                continue;
            }

            var entityErrors = new List<string>();
            foreach (var binding in bindings)
            {
                if (!TryBindPhysicalEvent(control, controlName, binding, entityErrors))
                {
                    // 失败原因已记录到 entityErrors
                }
            }

            if (entityErrors.Count == 0)
            {
                MarkBound(entity);
                boundCount++;
            }
            else
            {
                var joined = string.Join("; ", entityErrors);
                skipped.Add($"[{controlName}] {joined}");
                MarkFailed(entity, joined);
            }
        }

        // 手动点击诊断（Info/Warning，见 fde_log_messages.json）：结果总览 + 跳过明细统一经全局结构化日志单通道输出
        // （D4 收敛：原 _logger.Info 统计与 FrontEndDrivenLog 重复，保留结构化明细更利于诊断）。
        FrontEndDrivenLog.Log("EventBindingSystem_binding_result",
            new { entityCount = pendingEntities.Count, boundCount, skippedCount = skipped.Count, skipped });
        if (skipped.Count > 0)
        {
            FrontEndDrivenLog.Log("EventBindingSystem_binding_skipped",
                new { entityCount = pendingEntities.Count, boundCount, skippedCount = skipped.Count, skipped });
        }
    }

    private List<Entity> CollectPendingEntities()
    {
        return _entityManager.GetEntities<EventBindingsComponent>()
            .Where(e => _entityManager.TryGetComponent<EventBindingStateComponent>(e, out var state)
                        && state.State == EventBindingState.Pending)
            .ToList();
    }

    private void MarkBound(Entity entity)
    {
        if (!_entityManager.TryGetComponent<EventBindingStateComponent>(entity, out var stateComp))
            return;

        stateComp.State = EventBindingState.Bound;
        stateComp.RetryCount = 0;
        stateComp.ErrorMessage = string.Empty;
        _entityManager.SetComponent(entity, stateComp);
    }

    private void MarkFailed(Entity entity, string error)
    {
        if (!_entityManager.TryGetComponent<EventBindingStateComponent>(entity, out var stateComp))
            return;

        stateComp.State = EventBindingState.Failed;
        stateComp.ErrorMessage = error;
        _entityManager.SetComponent(entity, stateComp);
    }

    /// <summary>
    /// 物理真实挂接单个事件绑定：校验函数注册 → 事件名映射到物理事件（+= handler）→ handler 转发 ECS 事件链。
    /// 返回 false 表示未挂接（原因写入 errors）。
    /// </summary>
    private bool TryBindPhysicalEvent(
        Control control,
        string controlName,
        EventBindingComponent binding,
        List<string> errors)
    {
        var eventName = binding.EventName;
        var functionName = binding.FunctionName;

        if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(functionName))
        {
            errors.Add($"事件 '{eventName}' → 函数 '{functionName}' 名为空");
            return false;
        }

        if (_functionHub != null && !_functionHub.HasFunction(functionName))
        {
            errors.Add($"函数未注册: {functionName}（事件 {eventName}）");
            return false;
        }

        // handler 转发 ECS 事件链（参数绑定/函数执行由 EcsEventBus.BuildParameters → FunctionCallFacade 承担）
        void Raise() => _eventPort.Enqueue(controlName, eventName);

        // 物理挂接统一经 EventSubscriptionHelper 分派（D6 收敛）：事件名 → 控件类型映射唯一实现，
        // 与 InputCapturePort 值变更订阅共用，消除两处 switch 重复与实现漂移。
        if (EventSubscriptionHelper.TrySubscribe(control, eventName, Raise))
            return true;

        errors.Add($"事件 '{eventName}' 不支持物理挂接（控件类型 {control.GetType().Name}）");
        return false;
    }
}
