using System.Threading.Tasks;

namespace FrameworkAbstractions.WinFormsAbstractions;

/// <summary>
/// ECS 状态访问端口（M3·事件与命令收编）：命令 set/get 与交互回灌统一读写 ECS 状态。
/// 接口放共享层（BackEndDriven/FrontEndDriven 均引用），实现为 FrontEndDriven.EcsStateAccessor（经 AppServiceProvider 解析）。
/// 语义：set 写 ECS（ControlValueComponent）+ 触发投影同步；读分离为两条——
///   GetValue（命令/脚本真源读，ECS 优先）用于 get 命令/脚本回读；GetLiveValue（交互实时真值读，物理优先）用于事件分发参数。
/// GUI 输入经 InputCapturePort 回灌 ECS（与 GetLiveValue 的物理优先读互补，保证分发读到当前 UI 状态）。
/// </summary>
public interface IEcsStateAccessor
{
    /// <summary>
    /// 按控件名读 ECS 状态值（命令/脚本真源读）。策略="ECS 真源优先 + 物理兜底"：
    /// 用于 get 命令、脚本 set 后的回读、响应式订阅——以写入 ECS 的状态（含脚本 set 历史）为权威。
    /// </summary>
    string? GetValue(string controlName);

    /// <summary>
    /// 按控件名读"交互实时值"（事件分发参数真值读）。策略="物理控件真值优先 + ECS 兜底"：
    /// 用于事件绑定参数解析（用户触发事件时的当前 UI 状态即真值），避免空值/过期 ECS 状态遮蔽物理选择。
    /// 语义互补而非互斥：GetValue 服务命令/脚本（set 历史优先），GetLiveValue 服务交互分发（物理当前优先）。
    /// </summary>
    string? GetLiveValue(string controlName);

    /// <summary>按控件名写 ECS 状态值并触发投影同步（物理控件同步更新）。</summary>
    bool SetValue(string controlName, string? value);

    /// <summary>
    /// 按控件名<b>追加</b>写（outputMode=append 输出语义）：以目标控件当前显示值为基，
    /// 逐条追加一行后回写 ECS 并投影——终端/日志型目标（如 MainLayout 右侧 outputTextBox）多次运行结果累积可见。
    /// 基值取物理当前值（<see cref="GetLiveValue"/>）：终端可能被非 ECS 路径直写（如命令回显），
    /// 以物理真值为基可避免覆盖掉那部分内容。
    /// </summary>
    bool AppendValue(string controlName, string? value);
}

/// <summary>
/// 命令活动作用域上下文（Phase A·作用域感知寻址）：记录"命令当前应作用于哪个渲染 scope"。
/// 来源：脚本 load-config-show 加载/展示配置时置为目标 scope；命令寻址据此优先在活动 scope 内解析，
/// 消除「同名控件跨 scope 串扰」（如脚本点 ClineBrowser.clearButton 误中 MainLayout.clearButton）。
/// 实现注册于 AppServiceProvider（FrontEndDriven.ScopeContext）；缺省 CurrentScope=null 表示不做 scope 过滤（保持旧行为）。
/// </summary>
public interface IScopeContext
{
    /// <summary>当前活动渲染作用域名（null/空 = 不限定 scope，全局首匹配）。</summary>
    string? CurrentScope { get; set; }
}

/// <summary>
/// 交互事件分发结果状态（P4·事件分发执行确认）：命令 click 依此区分「已送达绑定链」与「未分发」，
/// 消除「入队即 PASS」的假绿——实体未命中 / 无匹配绑定均显式上报。
/// 语义口径：<see cref="Dispatched"/> 表示事件已送达绑定链并启动函数调用（异步执行），
/// 不承诺函数最终成败（成败由 EcsEventBus_Dispatch_function_failed 异步告警承载）。
/// </summary>
public enum EventDispatchOutcome
{
    /// <summary>已送达绑定链：实体命中 + ≥1 绑定匹配 + 函数调用已启动（异步执行）。</summary>
    Dispatched,

    /// <summary>目标控件名在渲染 ECS 中无对应实体（配置未渲染进 ECS 的典型信号）。</summary>
    EntityNotFound,

    /// <summary>实体存在但无匹配事件绑定（无绑定组件或绑定事件名不匹配）。</summary>
    NoBinding,

    /// <summary>已入队但因分发重入由外层分发循环消费，同步无法确知该事件结果。</summary>
    Dispatching,

    /// <summary>输入无效（空控件名/空事件名）或未能处理。</summary>
    Ignored
}

/// <summary>
/// 事件函数完成终态分类（框架级归因）：让命令层（click wait）能区分「函数错 vs 框架错」。
/// 与内核 InvocationOutcome 同构，但定义于共享契约层以避免跨工程枚举耦合。
/// </summary>
public enum EventFunctionResultKind
{
    /// <summary>函数正常完成。</summary>
    Succeeded,

    /// <summary>函数自身失败（返回失败/抛异常）→ 责任在函数。</summary>
    FunctionFault,

    /// <summary>框架绝对超时 → 责任在框架执行层。</summary>
    FrameworkTimedOut,

    /// <summary>框架空闲挂起 → 责任在框架执行层。</summary>
    FrameworkIdleHung,

    /// <summary>主动/外部取消。</summary>
    Cancelled,

    /// <summary>完成信号未落定 → 框架缺陷（明确可判）。</summary>
    SignalLost
}

/// <summary>
/// 事件函数完成载荷（R·反应式等待的完成信号）：成功与否 + 终态分类 + 错误详情。
/// 命令层据此给出可归因的失败消息，而非笼统「等待函数完成超时」。
/// </summary>
public sealed record EventFunctionOutcome(bool Succeeded, EventFunctionResultKind Kind, string? Error = null)
{
    public static EventFunctionOutcome Success()
        => new(true, EventFunctionResultKind.Succeeded, null);
}

/// <summary>
/// 交互层单一权威默认常量（Fix·超时收敛 D1/R1）：click/exec/按钮/工作流统一默认函数执行预算，
/// 供 EcsEventBus / ClickSystem / FunctionCallFacade / InvocationBudget 引用，避免各层独立「30s」漂移。
/// </summary>
public static class InteractionDefaults
{
    /// <summary>函数执行默认绝对预算（毫秒）。</summary>
    public const int DefaultFunctionTimeoutMs = 30_000;
}

/// <summary>事件分发确认载荷（Outcome + 命中的函数名/错误信息 + 在途函数完成信号 + 外部取消动作）。</summary>
public readonly record struct EventDispatchResult(
    EventDispatchOutcome Outcome,
    string? FunctionName = null,
    string? Error = null,
    /// <summary>
    /// 框架级反应式等待（R）：在途事件函数调用的真实完成信号——非轮询，函数终结（含成功/失败/超时）即触发。
    /// 命令层（如 click wait=true）经 Completion.Wait(timeout) 在真实完成上阻塞（与 AwaitValueSystem 同范式），
    /// 完成后可读 <see cref="EventFunctionOutcome"/>（成功与否 + 归因分类 + 错误详情）。
    /// 事件被重入分发未同步启动 → null。
    /// </summary>
    Task<EventFunctionOutcome>? Completion = null,
    /// <summary>调用关联 ID（opId）：贯穿内核/日志，供归因与观测。</summary>
    Guid OpId = default,
    /// <summary>
    /// 外部取消动作（Fix·双时钟对齐）：命令层在等待超时（click wait=true 的 waitTimeoutMs 到期）时调用，
    /// 经 CallerToken → 内核 op 链接令牌 → CancellationToken → ClineClient 杀进程树，提前终止在途函数，
    /// 消除「命令已 FAIL、函数仍在后台烧满预算」的孤儿窗口。事件被重入分发未同步启动 → null。
    /// </summary>
    Action? Cancel = null);

/// <summary>
/// 交互事件分发端口（M3·事件与命令收编）：命令 click 与 GUI 人手点击统一入 ECS 事件链。
/// 实现为 FrontEndDriven.EcsEventBus（实体事件队列 + 订阅分发 → FunctionCallFacade）。
/// </summary>
public interface IInteractionEventPort
{
    /// <summary>入队控件交互事件并尽力同步分发，返回分发确认（命令链据此判定 click 是否真正送达绑定链）。</summary>
    /// <param name="functionTimeoutMs">事件函数执行超时（毫秒）；null 使用框架默认。命令层 click-wait 的 timeout 经此透传，使「等待时长=函数执行预算」。</param>
    EventDispatchResult Enqueue(string controlName, string eventName, object? payload = null, int? functionTimeoutMs = null);

    /// <summary>消费事件队列：事件名 → 订阅（EventBindingsComponent 注册）→ 参数求值（读 ECS 状态）→ 函数执行。</summary>
    void DispatchPending();
}
