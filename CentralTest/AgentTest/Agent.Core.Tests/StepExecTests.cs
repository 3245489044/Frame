using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Nodes.Invocation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="StepExec"/> 唯一执行走廊定向测试（S3 Stage-1 / 9.3 §三）：
    /// 证明「空栈 = 直跑等价（零 handler 改动）」「中间件顺序固定、可插拔」「单步超时真实取消在途 handler 的 token（I3，非空转竞速）」。
    /// </summary>
    [TestClass]
    public class StepExecTests
    {
        [TestMethod]
        public async Task EmptyStack_Passthrough_ReturnsHandlerResult()
        {
            var result = await StepExec.InvokeAsync(
                new StepExecContext("fn"),
                _ => Task.FromResult<object?>("ok"),
                CancellationToken.None);

            Assert.AreEqual("ok", result, "空栈应直跑 handler，返回其结果（默认行为等价）");
        }

        [TestMethod]
        public async Task MiddlewareOrder_PreOuterFirst_InnerLast_PostReversed()
        {
            var order = new List<string>();
            StepExecMiddleware Mk(string tag) => async (ctx, handler, next, ct) =>
            {
                order.Add($"pre:{tag}");
                var r = await next(ct).ConfigureAwait(false);
                order.Add($"post:{tag}");
                return r;
            };

            var stack = new StepExecMiddleware[] { Mk("A"), Mk("B"), Mk("C") };
            await StepExec.InvokeAsync(new StepExecContext("fn"),
                _ => Task.FromResult<object?>("r"), CancellationToken.None, stack);

            CollectionAssert.AreEqual(
                new[] { "pre:A", "pre:B", "pre:C", "post:C", "post:B", "post:A" },
                order, "onion 顺序：前置按栈序、后置逆栈序");
        }

        [TestMethod]
        public async Task StepTimeout_WhenSet_CancelsInFlightHandlerToken()
        {
            using var outer = new CancellationTokenSource();
            var gotCancelled = false;
            var handlerToken = CancellationToken.None;

            Task<object?> Handler(CancellationToken t)
            {
                handlerToken = t;
                return LongWaitAsync(t);
            }

            async Task<object?> LongWaitAsync(CancellationToken t)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, t).ConfigureAwait(false); }
                catch (OperationCanceledException) { gotCancelled = true; throw; }
                return null;
            }

            var ctx = new StepExecContext("fn") { StepTimeout = TimeSpan.FromMilliseconds(30) };

            await AssertThrowsAsync<OperationCanceledException>(() =>
                StepExec.InvokeAsync(ctx, Handler, outer.Token, new[] { StepExecMiddlewares.StepTimeout() }));

            Assert.IsTrue(gotCancelled, "单步超时应对在途 handler 生效取消（I3，取消真实到达而非空转竞速）");
            Assert.IsTrue(handlerToken.CanBeCanceled, "handler 应收到 per-step linked token");
        }

        [TestMethod]
        public async Task NoTimeoutSet_StepTimeoutMiddleware_Passthrough()
        {
            var ctx = new StepExecContext("fn"); // StepTimeout 缺省 = 不启用单步超时
            var result = await StepExec.InvokeAsync(ctx, _ => Task.FromResult<object?>("v"),
                CancellationToken.None, new[] { StepExecMiddlewares.StepTimeout() });

            Assert.AreEqual("v", result, "未置 StepTimeout 时中间件应纯透传，不引入超时语义");
        }

        [TestMethod]
        public async Task OuterCancellation_ThrowsBeforeInvokingHandler()
        {
            using var outer = new CancellationTokenSource();
            outer.Cancel();
            var invoked = false;

            await AssertThrowsAsync<OperationCanceledException>(() =>
                StepExec.InvokeAsync(new StepExecContext("fn"),
                    _ => { invoked = true; return Task.FromResult<object?>(null); },
                    outer.Token));

            Assert.IsFalse(invoked, "外层已取消时不应再调用 handler");
        }

        /// <summary>MSTest 等价异步异常断言（框架无 Assert.ThrowsExceptionAsync；保持抛异常语义可测）。</summary>
        private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            try { await action().ConfigureAwait(false); }
            catch (TException) { return; }
            throw new AssertFailedException($"期望抛出 {typeof(TException).Name} 但未抛出。");
        }
    }
}
