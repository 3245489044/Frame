using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Threading;
using Share.EventBindingOrchestrator.Invocation;

namespace FrontEndDriven.Tests.Invocation;

/// <summary>
/// 函数调用内核回归测试：验证「函数错 vs 框架错」分类裁决、完成路径与超时路径。
/// </summary>
[TestClass]
public class InvocationKernelTests
{
    private sealed class MockAdapter : IInvocationAdapter
    {
        private readonly Func<IInvocationContext, Task<object?>> _fn;

        public MockAdapter(Func<IInvocationContext, Task<object?>> fn) => _fn = fn;

        public bool SupportsCancellation => true;

        public Task<object?> InvokeAsync(InvocationRequest request, IInvocationContext ctx)
            => _fn(ctx);
    }

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        // AEF 未装配时装配一次（生产真实路径）。已装配则复用，避免单例冲突。
        if (ThreadingBridge.AefScheduler == null)
        {
            var scheduler = new AsyncOperationScheduler(maxConcurrency: 4, idleWindow: TimeSpan.FromSeconds(30));
            var watchdog = new WatchdogService();
            watchdog.Start(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30));
            ThreadingBridge.InitializeAEF(scheduler, watchdog);
        }
    }

    private static InvocationRequest Req(int absoluteMs = 5000)
        => new("TestFn", new Dictionary<string, object?>(), InvocationSource.ExecCommand,
            new InvocationBudget(AbsoluteMs: absoluteMs, IdleMs: null));

    [TestMethod]
    public async Task Completed_函数正常返回_分类Completed()
    {
        var result = await InvocationKernel.ExecuteAsync(
            Req(), new MockAdapter(_ => Task.FromResult<object?>("ok")));

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ok", result.Output);
        Assert.IsNotNull(result.OpId);
        Assert.AreNotEqual(Guid.Empty, result.OpId);
    }

    [TestMethod]
    public async Task FunctionFault_函数抛异常_分类FunctionFault()
    {
        var result = await InvocationKernel.ExecuteAsync(
            Req(), new MockAdapter(_ => throw new InvalidOperationException("boom")));

        Assert.AreEqual(InvocationOutcome.FunctionFault, result.Outcome);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error ?? string.Empty, "boom");
    }

    [TestMethod]
    public async Task FunctionFault_执行服务返回失败_分类FunctionFault()
    {
        // 模拟经 DelegateInvocationAdapter 的失败传播（未注册执行服务 → FunctionFault）。
        var result = await InvocationKernel.ExecuteAsync(Req());

        // 测试宿主未注册 IEventExecutionService → 默认适配器抛错 → FunctionFault。
        Assert.AreEqual(InvocationOutcome.FunctionFault, result.Outcome);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task FrameworkTimedOut_绝对预算到期_分类FrameworkTimedOut()
    {
        // 适配器永久等待；内核 300ms 绝对预算 → 调度器取消 → TimedOut。
        var result = await InvocationKernel.ExecuteAsync(
            Req(absoluteMs: 300),
            new MockAdapter(async ctx =>
            {
                await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
                return null;
            }));

        Assert.AreEqual(InvocationOutcome.FrameworkTimedOut, result.Outcome);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error ?? string.Empty, "框架绝对超时");
    }

    [TestMethod]
    public async Task Cancelled_外部取消_分类Cancelled而非TimedOut()
    {
        // Fix·归因修正回归：命令层等待超时经 CallerToken 取消，应归 Cancelled，
        // 不得被误判为「框架绝对超时」（外部取消的锅扣到框架执行层）。
        using var cts = new CancellationTokenSource();
        var req = new InvocationRequest(
            "TestFn",
            new Dictionary<string, object?>(),
            InvocationSource.CommandClick,
            new InvocationBudget(AbsoluteMs: 60_000, IdleMs: null),
            CallerToken: cts.Token);

        var invoke = InvocationKernel.ExecuteAsync(req, new MockAdapter(async ctx =>
        {
            await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return null;
        }));

        await Task.Delay(100);          // 让 op 进入 Running
        cts.Cancel();                    // 外部取消（模拟 click wait 超时）
        var result = await invoke;

        Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome);
        Assert.IsFalse(result.IsSuccess);
    }
}
