using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.State.Runtime;
using Agent.Core.TaskPool;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 确定性核心单源一致性护栏（Phase 0/1 契约）：
    /// 验证「派生共享核心引用 → 轮内经租约推进 → 收割标量」往返后，根 SchedulerState 正确反映本轮全部变更。
    /// 重构只允许改「状态承载 / 流转方式」，不得改变根状态对外语义 —— 本套件即回归护栏。
    /// </summary>
    [TestClass]
    public class CoreConsistencyGuardTests
    {
        private static AgentTaskItem NewRoot()
        {
            var s = new AgentTaskItem { Round = 1, Scope = Scope.LoopBody };
            s.Token.Max = 3;
            return s;
        }

        /// <summary>镜像 AgentRunner.CreateLeaseContext（注入共享核心对象引用 + 播种标量）。</summary>
        private static AxiomContext DeriveSharedLease(AgentTaskItem s)
            => new AxiomContext(s.Data, s.Budget, s.Token, s.Flag)
            {
                Round = s.Round,
                LastEval = s.LastEval,
                Scope = s.Scope,
            };

        /// <summary>镜像 AgentRunner.ComputeDeterministicCore 的确定性推进（评估入账 + 进度 + 熔断 + halt + 轮次自增）。</summary>
        private static void AdvanceViaLease(AxiomContext lease, EvalOutcome eval, ErrorClass? err)
        {
            lease.LastEval = eval;
            ProgressUpdater.Apply(lease, eval);
            BudgetEscalator.Apply(lease, err, eval);
            if (lease.Budget.IsTripped) lease.Flag.SetHalt();
            lease.Round++;
        }

        /// <summary>镜像 AgentRunner.HarvestDeterministicCore（仅收割标量 + 连续失败计数）。</summary>
        private static void Harvest(AgentTaskItem s, AxiomContext lease)
        {
            s.Round = lease.Round;
            s.LastEval = lease.LastEval;
            s.Scope = lease.Scope;
            s.ContinuousFailures = ContinuousFailureRule.Next(s.ContinuousFailures, lease.LastEval);
        }

        [TestMethod]
        public void SharedLease_RoundTrip_MutatesRootSingleSource()
        {
            var root = NewRoot();
            var lease = DeriveSharedLease(root);

            // ① 共享：核心对象与 Data 均为根实例（单一事实源，无双副本）。
            Assert.AreSame(root.Budget, lease.Budget);
            Assert.AreSame(root.Token, lease.Token);
            Assert.AreSame(root.Flag, lease.Flag);
            Assert.AreSame(root.Data, lease.Data);

            // ② 轮内失败推进（ExecutionFailure）。
            AdvanceViaLease(lease, EvalOutcome.Fail, ErrorClass.ExecutionFailure);

            // ③ 收割标量。
            Harvest(root, lease);

            // 根必须正确反映：轮次自增 / 预算升级 / token 前进 / LastEval / 连续失败 +1。
            Assert.AreEqual(2, root.Round);
            Assert.AreEqual(1, root.Budget.Level);
            Assert.AreEqual(1, root.Token.Value);
            Assert.AreEqual(EvalOutcome.Fail, root.LastEval);
            Assert.AreEqual(1, root.ContinuousFailures);
        }

        [TestMethod]
        public void SharedLease_PassRound_DecaysBudgetAndRetreatsToken()
        {
            var root = NewRoot();
            root.Budget.Escalate();        // level 1
            root.Token.Advance();          // value 1
            root.ContinuousFailures = 1;
            var lease = DeriveSharedLease(root);

            AdvanceViaLease(lease, EvalOutcome.Pass, null);
            Harvest(root, lease);

            Assert.AreEqual(0, root.Budget.Level);        // 成功衰减
            Assert.AreEqual(0, root.Token.Value);         // 成功回退
            Assert.AreEqual(EvalOutcome.Pass, root.LastEval);
            Assert.AreEqual(0, root.ContinuousFailures);  // 连续失败归零
        }

        [TestMethod]
        public void SharedLease_Irrecoverable_TripsHaltOnRoot()
        {
            var root = NewRoot();
            var lease = DeriveSharedLease(root);

            AdvanceViaLease(lease, EvalOutcome.Fail, ErrorClass.Irrecoverable);

            // 共享引用：irrecoverable 的 halt/预算升级直写根对象。
            Assert.IsTrue(root.Flag.Halt);
            Assert.AreEqual(3, root.Budget.Level);
        }

        [TestMethod]
        public void DefaultConstructors_CreateIndependentCore()
        {
            // 向后兼容：默认构造 / data-only 构造仍新建独立核心对象（互不影响）。
            var a = new AxiomContext();
            var b = new AxiomContext(new Dictionary<string, object>());

            a.Budget.Escalate();
            a.Token.Advance();
            a.Flag.SetHalt();

            Assert.AreEqual(0, b.Budget.Level);
            Assert.AreEqual(0, b.Token.Value);
            Assert.IsFalse(b.Flag.Halt);
            Assert.AreNotSame(a.Budget, b.Budget);
        }
    }
}
