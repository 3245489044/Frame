#nullable enable
using System;

namespace WorkflowExecution.Execution
{
    /// <summary>
    /// 统一执行单元 FuncCall 的三 kind 分类（T7）—— 对齐《任务工作流化-Agent任务历史操作与FuncCall统一治理-方案》§3.1：
    /// <c>internal</c> = 工作流内置函数（<see cref="IWorkflowRegistry"/> 登记；<c>fn=</c> 声明面）；
    /// <c>external</c> = 外部/本地工具（工具执行面真源 = 工具注册表；装配期注入）；
    /// <c>composition</c> = 纯 FuncCall 套接（<see cref="FuncCall.PipelineGroup"/> 子管线；不触执行面）。
    /// </summary>
    public enum FuncCallKind
    {
        /// <summary>工作流内置函数（<c>fn=</c>；执行面 = IWorkflowRegistry）。</summary>
        Internal,

        /// <summary>外部 / 本地工具（执行面 = 工具注册表，装配期注入；单源复用 LlmToolRegistry.TryInvoke）。</summary>
        External,

        /// <summary>组合族（子管线，由管线递归承接，不经执行分派）。</summary>
        Composition,
    }

    /// <summary>FuncCall 单次分派结果（W-3 单源分派的返回值；<see cref="Ok"/>=false 时 <see cref="Error"/> 携带可观测归因，禁静默）。</summary>
    public sealed record FuncCallResult(bool Ok, string FuncName, FuncCallKind Kind, object? Value, string? Error);

    /// <summary>
    /// FuncCall 执行分派**唯一入口**（W-3）—— internal → 工作流注册表；external → 工具执行面。
    /// <para>组合（<see cref="FuncCallKind.Composition"/>）由管线递归承接（见 <see cref="FuncCallRun"/>），不经本端口。</para>
    /// <para>工具执行面单源：external handler **复用** <c>LlmToolRegistry.TryInvoke</c> 的适配器，
    /// 由组合根注入（本层零 Usharp / 零 Agent 依赖，守 StorageUsharpRefs=0）。</para>
    /// </summary>
    public interface IFuncCallDispatcher
    {
        /// <summary>按 kind 分派一次 FuncCall；不抛（缺装配 / 未注册等经 <see cref="FuncCallResult.Ok"/>=false 可见化）。</summary>
        FuncCallResult Dispatch(FuncCall call, FuncExecutionContext context, FuncCallKind kind);
    }

    /// <summary>
    /// 默认分派实现（Frame / WorkFlowMethod 层）：internal 走 <see cref="IWorkflowRegistry"/>；
    /// external 走**装配期注入**的外部执行委托。未装配 external handler ⇒ 显式失败（不回退静默）。
    /// </summary>
    public sealed class WorkflowFuncCallDispatcher : IFuncCallDispatcher
    {
        private readonly IWorkflowRegistry? _internalRegistry;
        private readonly Func<FuncCall, FuncExecutionContext, object?>? _externalHandler;

        /// <param name="internalRegistry">工作流内置函数注册表（<c>fn=</c> 真源）；null = 未装配。</param>
        /// <param name="externalHandler">外部工具执行委托（组合根注入，复用工具注册表 TryInvoke）；null = 未装配。</param>
        public WorkflowFuncCallDispatcher(
            IWorkflowRegistry? internalRegistry,
            Func<FuncCall, FuncExecutionContext, object?>? externalHandler)
        {
            _internalRegistry = internalRegistry;
            _externalHandler = externalHandler;
        }

        /// <inheritdoc />
        public FuncCallResult Dispatch(FuncCall call, FuncExecutionContext context, FuncCallKind kind)
        {
            if (call is null) throw new ArgumentNullException(nameof(call));
            if (context is null) throw new ArgumentNullException(nameof(context));

            switch (kind)
            {
                case FuncCallKind.External:
                    if (_externalHandler is null)
                        return new FuncCallResult(false, call.FuncName, kind, null,
                            "external 工具执行面未装配（组合根须注入 LlmToolRegistry.TryInvoke 适配器）。");
                    return new FuncCallResult(true, call.FuncName, kind, _externalHandler(call, context), null);

                case FuncCallKind.Composition:
                    return new FuncCallResult(false, call.FuncName, kind, null,
                        "composition kind 须由管线递归承接（PipelineGroup），不应到达分派器。");

                default:
                    if (_internalRegistry is null)
                        return new FuncCallResult(false, call.FuncName, kind, null, "internal 工作流注册表未装配。");
                    // 未登记 → 保持既有语义（null 值，不抛）；已登记 → 调用（未找到异常原样上抛，由管线收口）。
                    if (!_internalRegistry.HasFunction(call.FuncName))
                        return new FuncCallResult(true, call.FuncName, kind, null, null);
                    return new FuncCallResult(true, call.FuncName, kind, _internalRegistry.Call(call.FuncName, context), null);
            }
        }
    }
}
