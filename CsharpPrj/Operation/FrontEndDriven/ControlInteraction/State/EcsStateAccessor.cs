
#nullable enable

using System;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Utils;
using Modules.ComponentData.Extensions;
using TConfig.Controls;

namespace FrontEndDriven.ControlInteraction.State;

/// <summary>
/// ECS 状态访问器（M3·事件与命令收编 + P2·值语义契约接管）：命令 set/get 与 GUI 输入回灌统一读写 ECS 状态。
/// 真源方向：写 ECS（ControlValueComponent）→ 契约投影物理；读分离为两条语义——
///   <see cref="GetValue"/>（命令/脚本真源读，ECS 优先 + 物理兜底）；
///   <see cref="GetLiveValue"/>（交互分发实时真值读，物理优先 + ECS 兜底，避免空/过期 ECS 遮蔽物理选择）。
/// 值语义经 <see cref="IControlService"/>（ControlValueBridge / ValueContractRegistry）——ListView 等选择型控件的
/// "匹配选中 + 回读 Tag 规范值"由契约承载，不再散落控件类型特判（原 ListView 专用分支已删除）。
/// </summary>
public sealed class EcsStateAccessor : IEcsStateAccessor
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IControlService _valueService;
    private readonly EntityQueryService _entityQuery;

    public EcsStateAccessor(
        EntityManager entityManager,
        ControlPool controlPool,
        IControlService valueService,
        EntityQueryService entityQuery)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _valueService = valueService ?? throw new ArgumentNullException(nameof(valueService));
        _entityQuery = entityQuery ?? throw new ArgumentNullException(nameof(entityQuery));
    }

    public string? GetValue(string controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return null;

        var entity = FindEntityByName(controlName);
        if (!entity.IsValid) return null;

        // 真源优先：ECS 状态（ControlValueComponent）→ 物理契约读 → ControlInfo 兜底
        return ReadValue(entity, ecsFirst: true);
    }

    /// <summary>
    /// 交互实时值读（事件分发参数真值读）：物理控件当前真值优先（值语义契约），
    /// 无物理控件/未渲染则回退 ECS 状态，再回退 ControlInfoComponent.Text。
    /// 与 <see cref="GetValue"/>（ECS 真源优先）语义互补：本方法服务用户触发事件时的"当前 UI 状态"，
    /// 避免空值/过期 ECS 状态遮蔽物理选择（如下拉框已选但 ECS 未回灌/为空的场景）。
    /// </summary>
    public string? GetLiveValue(string controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return null;

        var entity = FindEntityByName(controlName);
        if (!entity.IsValid) return null;

        // 物理真值优先：物理契约读 → ECS 状态兜底 → ControlInfo 兜底
        return ReadValue(entity, ecsFirst: false);
    }

    /// <summary>
    /// 公共读链（GetValue/GetLiveValue 近复制收敛）：三段式回退（ECS 状态 / 物理契约读 / ControlInfo.Text），
    /// 由 <paramref name="ecsFirst"/> 决定 ECS 与物理的优先级顺序：
    /// ecsFirst=true（真源读，GetValue）ECS 优先；ecsFirst=false（实时真值读，GetLiveValue）物理优先。
    /// 尾部均为 ControlInfo.Text 兜底。兜底/异常处理单点维护，消除两条链各自复制。
    /// </summary>
    private string? ReadValue(Entity entity, bool ecsFirst)
    {
        // P2·契约读：物理控件经值语义契约（默认语义）读取（如 ListView 选中项 Tag??Text）
        string? ReadPhysical()
        {
            try
            {
                var control = _controlPool.GetControl(entity);
                if (control != null)
                {
                    var value = _valueService.GetValue(control);
                    if (value != null)
                        return value.ToString();
                }
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log(ecsFirst
                        ? "EcsStateAccessor_GetValue_contract_failed"
                        : "EcsStateAccessor_GetLiveValue_contract_failed",
                    new { controlName = _controlPool.GetControlName(entity), error = ex.Message });
            }
            return null;
        }

        string? ReadEcs()
            => _entityManager.TryGetComponent<ControlValueComponent>(entity, out var valueComp)
                && valueComp.Value != null
                ? valueComp.Value
                : null;

        if (ecsFirst)
        {
            if (ReadEcs() is { } ecsValue) return ecsValue;
            if (ReadPhysical() is { } physicalValue) return physicalValue;
        }
        else
        {
            if (ReadPhysical() is { } physicalValue) return physicalValue;
            if (ReadEcs() is { } ecsValue) return ecsValue;
        }

        if (_entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info))
            return info.Text;

        return null;
    }

    /// <summary>
    /// 追加写（outputMode=append 输出语义，终端/日志型目标专用）：
    /// 读取目标控件当前显示值（物理优先，见 <see cref="GetLiveValue"/>）→ 追加一行 → 复用 <see cref="SetValue"/>
    /// 回写 ECS 并投影。基值取物理真值，避免覆盖经非 ECS 路径直写的内容（如命令回显）。
    /// 空文本按空行追加（保持「每次运行一行」的节奏，不静默丢弃）。
    /// </summary>
    public bool AppendValue(string controlName, string? value)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return false;

        var line = value ?? string.Empty;
        var current = GetLiveValue(controlName);

        string next;
        if (string.IsNullOrEmpty(current))
            next = line;
        else if (current.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                 || current.EndsWith("\n", StringComparison.Ordinal))
            next = current + line;
        else
            next = current + Environment.NewLine + line;

        var written = SetValue(controlName, next);
        FrontEndDrivenLog.Log("EcsStateAccessor_AppendValue",
            new { controlName, appendedLength = line.Length, totalLength = next.Length, written });
        if (written) FollowTail(controlName);
        return written;
    }

    /// <summary>
    /// 终端跟随：写后把插入点移到文本末尾并滚动至光标——追加输出像控制台一样始终显示最新一行。
    /// 仅对多行文本框（<see cref="TextBoxBase"/>）生效；其它控件类型直接跳过（无滚动语义）。
    /// </summary>
    private void FollowTail(string controlName)
    {
        var entity = FindEntityByName(controlName);
        if (!entity.IsValid) return;

        var control = _controlPool.GetControl(entity);
        if (control is not TextBoxBase textBox) return;

        RunOnUiThread(control, () =>
        {
            textBox.SelectionStart = textBox.TextLength;
            textBox.ScrollToCaret();
        });
    }

    public bool SetValue(string controlName, string? value)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return false;

        var entity = FindEntityByName(controlName);
        if (!entity.IsValid) return false;

        // 真源写入：ECS 状态（唯一事实）
        _entityManager.AddOrSetComponent(entity, new ControlValueComponent { Value = value });

        string? effective = value;
        bool controlFound = false;
        string? physicalAfter = null;
        string? projectionError = null;

        // 投影：契约写（默认语义）+ 写后规范值回写 ECS（保证"写入 ECS 的值 = 物理读回的值"一致；
        // 如 set MathFunctions → 匹配选中 → 规范值回写为 Tag 完整路径）
        try
        {
            var control = _controlPool.GetControl(entity);
            if (control == null)
            {
                // E2 修复：控件未入池时物理投影未发生，返回 false 反映「ECS 已写但物理未投影」——
                // 旧实现返回 true 使调用方误判投影成功（返回值契约失效）。
                FrontEndDrivenLog.Log("EcsStateAccessor_SetValue_no_control",
                    new { controlName, value, entityId = entity.Id.ToString() });
                PublishValueChange(controlName, effective);
                LogProjection(controlName, entity, value, controlFound: false, physicalAfter: "(no control)", projection: "skipped", error: null);
                return false;
            }
            controlFound = true;

            // D5 收敛：单一服务入口——契约写（默认语义）+ 契约未覆盖类型经物理内核（IControlService 内部兜底）。
            // 跨线程安全：命令/脚本在后台线程执行，物理控件写与写后回读须封送到 UI 线程（Control.Invoke），
            // 否则 ECS 投影遇 TreeView/ListView 等原生控件抛「正在从错误的线程调用」跨线程异常（StateSnapshot 确定性约束）。
            string? canonical = null;
            RunOnUiThread(control, () =>
            {
                canonical = _valueService.SetValue(control, value);
                physicalAfter = TryReadPhysicalText(control);
            });
            if (canonical != null && !string.Equals(canonical, value, StringComparison.Ordinal))
            {
                _entityManager.AddOrSetComponent(entity, new ControlValueComponent { Value = canonical });
                effective = canonical;
                FrontEndDrivenLog.Log("EcsStateAccessor_SetValue_canonical",
                    new { controlName, value, canonical });
            }
        }
        catch (Exception ex)
        {
            projectionError = ex.Message;
            FrontEndDrivenLog.Log("EcsStateAccessor_SetValue_projection_failed",
                new { controlName, entityId = entity.Id.ToString(), error = ex.Message });
            LogProjection(controlName, entity, value, controlFound, physicalAfter: "(exception)", projection: "failed", error: ex.Message);
            return false;
        }

        FrontEndDrivenLog.Log("EcsStateAccessor_SetValue",
            new { controlName, value, entityId = entity.Id.ToString() });

        // 输出联动诊断（Info）：物理投影是否真正写入可见控件、写后物理值——定位「ECS 已写但界面不刷新」断点。
        LogProjection(controlName, entity, effective, controlFound, physicalAfter, projection: "written", error: null);

        PublishValueChange(controlName, effective);

        return true;
    }

    /// <summary>
    /// 物理投影封送：控件在非 UI 线程（命令/脚本执行线程）时经统一封送路径写/读，避免跨线程异常（StateSnapshot）。
    /// E1 修复：弃用团队已明令移走的 <c>Control.Invoke</c>（同步阻塞死锁），改用统一权威
    /// <see cref="UiThreadHelper.DispatchIfNeeded"/>（= UiDispatcherCore：BeginInvoke + TCS + 5s 超时，已在线程/无目标直跑）。
    /// </summary>
    private static void RunOnUiThread(object control, Action action)
        => UiThreadHelper.DispatchIfNeeded(control, action);

    /// <summary>输出联动诊断日志（见 fde_log_messages.json，Debug）：报告物理投影结果与写后物理真值。
    /// value/physicalAfter 超长（如 htmlviewer 整页 HTML）截断为摘要 + 长度计数，避免几 KB 内容刷屏。</summary>
    private void LogProjection(string controlName, Entity entity, string? value, bool controlFound, string? physicalAfter, string projection, string? error)
    {
        FrontEndDrivenLog.Log("EcsStateAccessor_SetValue_projection",
            new
            {
                controlName,
                entityId = entity.Id.ToString(),
                value = TruncatePreview(value),
                controlFound,
                physicalAfter = TruncatePreview(physicalAfter),
                projection,
                error
            });
    }

    /// <summary>超长文本截断为预览（前 200 字符 + 长度计数）；空/短文本原样。</summary>
    private static string? TruncatePreview(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= 200) return text;
        return text[..200] + $"…(len={text.Length})";
    }

    /// <summary>反射读物理控件当前 Text（写后回读），用于确认投影是否落到可见控件。</summary>
    private static string? TryReadPhysicalText(object control)
    {
        try
        {
            return control.GetType().GetProperty("Text")?.GetValue(control)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 发布桥：控件值写回 ECS 后发布到响应式 ComponentDataStore（键 control:{name}），
    /// 供 await 等响应式消费者订阅（事件驱动等待，非轮询）。
    /// </summary>
    private void PublishValueChange(string controlName, string? value)
    {
        try
        {
            _entityManager.GetComponentDataStore().Set($"control.{controlName}", value);
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("EcsStateAccessor_PublishValueChange_failed",
                new { controlName, error = ex.Message });
        }
    }

    private Entity FindEntityByName(string controlName)
        => EntityLookup.FindByName(_entityQuery, controlName);
}

