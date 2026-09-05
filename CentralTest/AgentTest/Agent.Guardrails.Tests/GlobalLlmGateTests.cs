using Agent.Guardrails;
using Agent.Guardrails.Gate;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Guardrails.Tests
{
    [TestClass]
    public class GlobalLlmGateTests
    {
        private GlobalLlmGate CreateGate() => new();

        [TestMethod]
        public void KillSwitch_Halt_DeniesAllCalls_AndReset_Recovers()
        {
            var gate = CreateGate();
            gate.Halt("人工中止");

            using var scope = gate.OpenScope("r1");
            var denied = scope.EnterCall(null, null);

            Assert.IsFalse(denied.IsAllowed);
            Assert.AreEqual(GlobalGateLimitKind.Halt, denied.LimitKind);
            Assert.IsTrue(gate.IsHalted);

            gate.Reset();
            Assert.IsFalse(gate.IsHalted);
            using var scope2 = gate.OpenScope("r2");
            Assert.IsTrue(scope2.EnterCall(null, null).IsAllowed);
        }

        [TestMethod]
        public void GlobalCallBudget_Exceeds_IsDenied()
        {
            var gate = CreateGate();
            gate.Configure(new GlobalGateLimits { MaxCallsGlobal = 1 });

            using var s1 = gate.OpenScope("r1");
            Assert.IsTrue(s1.EnterCall(null, null).IsAllowed);
            s1.ExitCall(GlobalGateUsage.Empty);

            // 第二个 run 的调用聚合超限 → 拒绝（全局聚合不可被单 run 绕过）
            using var s2 = gate.OpenScope("r2");
            var decision = s2.EnterCall(null, null);
            Assert.IsFalse(decision.IsAllowed);
            Assert.AreEqual(GlobalGateLimitKind.CallBudget, decision.LimitKind);
        }

        [TestMethod]
        public void RunCallBudget_Exceeds_IsDenied()
        {
            var gate = CreateGate();
            using var scope = gate.OpenScope("r1", new GlobalGateLimits { MaxCallsPerRun = 2 });

            Assert.IsTrue(scope.EnterCall(null, null).IsAllowed); scope.ExitCall(GlobalGateUsage.Empty);
            Assert.IsTrue(scope.EnterCall(null, null).IsAllowed); scope.ExitCall(GlobalGateUsage.Empty);
            var decision = scope.EnterCall(null, null);
            Assert.IsFalse(decision.IsAllowed);
            Assert.AreEqual(GlobalGateLimitKind.CallBudget, decision.LimitKind);
        }

        [TestMethod]
        public void GlobalTokenBudget_Exceeds_IsDenied()
        {
            var gate = CreateGate();
            gate.Configure(new GlobalGateLimits { MaxOutputTokensGlobal = 100 });

            using var scope = gate.OpenScope("r1");
            Assert.IsTrue(scope.EnterCall(null, null).IsAllowed);
            scope.ExitCall(new GlobalGateUsage(InputTokens: 0, OutputTokens: 100));

            var denied = scope.EnterCall(null, null);
            Assert.IsFalse(denied.IsAllowed);
            Assert.AreEqual(GlobalGateLimitKind.OutputTokenBudget, denied.LimitKind);
        }

        [TestMethod]
        public void Concurrency_Throttle_DeniesSecondInFlight()
        {
            var gate = CreateGate();
            gate.Configure(new GlobalGateLimits { MaxConcurrentCallsGlobal = 1 });

            using var scope = gate.OpenScope("r1");
            Assert.IsTrue(scope.EnterCall(null, null).IsAllowed); // 第 1 个在途，未 Exit
            Assert.IsFalse(scope.EnterCall(null, null).IsAllowed); // 并发上限 1 → 拒绝
            Assert.AreEqual(GlobalGateLimitKind.Concurrency, scope.EnterCall(null, null).LimitKind);

            scope.ExitCall(GlobalGateUsage.Empty); // 释放
            Assert.IsTrue(scope.EnterCall(null, null).IsAllowed);
        }

        [TestMethod]
        public void Scope_Dispose_RestoresPreviousScope_Nested()
        {
            var gate = CreateGate();

            using var outer = gate.OpenScope("outer");
            Assert.AreEqual("outer", gate.CurrentScope?.RunId);

            using (var inner = gate.OpenScope("inner"))
            {
                Assert.AreEqual("inner", gate.CurrentScope?.RunId);
            }

            Assert.AreEqual("outer", gate.CurrentScope?.RunId, "inner 释放后应恢复 outer");
        }

        [TestMethod]
        public void Host_RunScopedAsync_RunsInScope_And_GetState_Reflects()
        {
            GlobalLlmGateHost.Reset();
            GlobalLlmGateHost.Configure(new GlobalGateLimits { MaxCallsGlobal = 10 });

            var value = GlobalLlmGateHost.RunScopedAsync("h1", async ct =>
            {
                var gate = GlobalLlmGateHost.Gate;
                var d = gate.CurrentScope!.EnterCall(null, null);
                gate.CurrentScope!.ExitCall(new GlobalGateUsage(InputTokens: 5, OutputTokens: 3));
                return d.IsAllowed;
            }).GetAwaiter().GetResult();

            Assert.IsTrue(value);
            var state = GlobalLlmGateHost.GetState();
            Assert.AreEqual(1, state.GlobalCalls);
            Assert.AreEqual(5, state.GlobalInputTokens);
            Assert.AreEqual(3, state.GlobalOutputTokens);
            GlobalLlmGateHost.Reset();
        }

        [TestMethod]
        public void Host_CreateAsyncCheck_ExhaustsGlobalCallBudget_ThrowsBlocked()
        {
            var gate = GlobalLlmGateHost.Gate;
            gate.Reset();
            gate.Configure(new GlobalGateLimits { MaxCallsGlobal = 2 });
            var check = GlobalLlmGateHost.CreateAsyncCheck();

            // 前 2 次放行并计数
            check(default).GetAwaiter().GetResult();
            check(default).GetAwaiter().GetResult();
            Assert.AreEqual(2, gate.GetState().GlobalCalls);

            // 第 3 次超全局调用预算 → 拦截
            var ex = AssertThrows<GlobalGateBlockedException>(() => check(default).GetAwaiter().GetResult());
            Assert.AreEqual(GlobalGateLimitKind.CallBudget, ex.Decision.LimitKind);
            gate.Reset();
        }

        [TestMethod]
        public void Host_CreateAsyncCheck_KillSwitch_BlocksImmediately()
        {
            var gate = GlobalLlmGateHost.Gate;
            gate.Reset();
            gate.Configure(GlobalGateLimits.Unlimited);
            GlobalLlmGateHost.Halt("应急中止");
            try
            {
                var check = GlobalLlmGateHost.CreateAsyncCheck();
                var ex = AssertThrows<GlobalGateBlockedException>(() => check(default).GetAwaiter().GetResult());
                Assert.AreEqual(GlobalGateLimitKind.Halt, ex.Decision.LimitKind);
                Assert.IsTrue(ex.IsHalt);
            }
            finally
            {
                gate.Reset();
            }
        }

        [TestMethod]
        public void Host_RunScopedAsync_RunBudget_Enforced_AcrossCalls()
        {
            var gate = GlobalLlmGateHost.Gate;
            gate.Reset();
            gate.Configure(GlobalGateLimits.Unlimited);

            var calls = 0;
            GlobalLlmGateHost.RunScopedAsync("budget-run",
                _ =>
                {
                    // run 级预算：最多 1 次调用
                    Assert.IsTrue(gate.CurrentScope!.EnterCall(null, null).IsAllowed);
                    gate.CurrentScope.ExitCall(GlobalGateUsage.Empty);
                    Assert.AreEqual(GlobalGateLimitKind.CallBudget, gate.CurrentScope.EnterCall(null, null).LimitKind);
                    calls++;
                    return System.Threading.Tasks.Task.CompletedTask;
                }, runLimits: new GlobalGateLimits { MaxCallsPerRun = 1 }).GetAwaiter().GetResult();

            Assert.AreEqual(1, calls);
            gate.Reset();
        }

        /// <summary>MSTest 版本不含 Assert.ThrowsException；本地辅助（验证指定异常抛出并返回实例）。</summary>
        private static TException AssertThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                Assert.Fail($"期望 {typeof(TException).Name}，实际抛出 {ex.GetType().Name}: {ex.Message}");
            }
            Assert.Fail($"期望抛出 {typeof(TException).Name}，但未抛出");
            return null!;
        }
    }
}
