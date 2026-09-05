
using Agent.Calculate.Estimation;
using Agent.Contract.Boundaries;
using Agent.Axioms.Boundaries;
using Agent.Core.Boundaries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 边界框架纯函数单测：逻辑评估（上下文/信息增益/复杂度 + 强信号）、空间评估（允许/越界/禁用路径）、
    /// 重构器（动作→提示）、上下文估算（估算 vs 真实 token）。
    /// </summary>
    [TestClass]
    [TestCategory("BoundaryFramework")]
    public class BoundaryFrameworkTests
    {
        private static BoundarySignal HealthySignal(int round = 1)
            => new(round, 5, 1000, "ok", EvalOutcome.Pass, null, 0, 0, 3);

        [TestMethod]
        public void LogicalEvaluator_健康信号_不应触发()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal();

            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 800);

            Assert.IsFalse(a.Triggered, "健康信号不应触发边界重构");
            Assert.AreEqual(BoundaryActionKind.None, a.Action);
            Assert.IsTrue(a.Score < config.Logical.ReconstructThreshold, $"Score 应低于阈值，实际 {a.Score}");
        }

        [TestMethod]
        public void LogicalEvaluator_上下文压力超限_应触发且建议Compact()
        {
            var config = BoundaryConfig.Default;
            var estimate = new ContextEstimate(config.Logical.ContextLimitChars, null);
            var signal = HealthySignal();

            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, estimate, priorResultLength: 100);

            Assert.IsTrue(a.ContextPressure >= config.Logical.ReconstructThreshold, "上下文压力应越界");
            Assert.IsTrue(a.Triggered, "上下文压力越界应触发");
            Assert.AreEqual(BoundaryActionKind.Compact, a.Action, "默认首动作应为 compact");
        }

        [TestMethod]
        public void LogicalEvaluator_信息增益趋零_应触发()
        {
            var config = BoundaryConfig.Default;
            var signal = new BoundarySignal(2, 5, 5, "ok", EvalOutcome.Partial, null, 0, 0, 3);
            var estimate = ContextEstimate.Empty;

            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, estimate, priorResultLength: 5000);

            Assert.IsTrue(a.InfoGainRate < config.Logical.InfoGainMinRate, "信息增益速率应低于下限");
            Assert.IsTrue(a.Triggered, "增益趋零应触发边界重构");
        }

        [TestMethod]
        public void LogicalEvaluator_上下文窗口已满强信号_应短路触发()
        {
            var config = BoundaryConfig.Default;
            var signal = HealthySignal() with { StopReason = "model_context_window_exceeded" };

            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 0);

            Assert.IsTrue(a.Triggered, "stopReason=context_exceeded 应强信号触发");
            Assert.AreEqual(BoundaryActionKind.Compact, a.Action);
        }

        [TestMethod]
        public void LogicalEvaluator_复杂度耗尽且无进展_应触发()
        {
            var config = BoundaryConfig.Default;
            var signal = new BoundarySignal(3, 5, 100, "ok", EvalOutcome.Partial, null, 2, 3, 3);

            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 100);

            Assert.IsTrue(a.Triggered, "token 耗尽且无进展应触发");
        }

        [TestMethod]
        public void SpatialEvaluator_允许根内_应允许()
        {
            var config = BoundaryConfig.Default;
            config.Spatial.BaseRoot = @"d:\repo";
            config.Spatial.AllowedRoots = new() { @"d:\repo" };

            var a = SpatialBoundaryEvaluator.Resolve(config.Spatial, @"d:\repo\src");

            Assert.IsTrue(a.SpatialAllowed, "允许根内路径应允许");
            Assert.IsTrue(a.EffectiveRoot!.StartsWith(@"d:\repo"), "有效根应在允许根内");
        }

        [TestMethod]
        public void SpatialEvaluator_越界_应拒绝并回退安全根()
        {
            var config = BoundaryConfig.Default;
            config.Spatial.BaseRoot = @"d:\repo";
            config.Spatial.AllowedRoots = new() { @"d:\repo" };

            var a = SpatialBoundaryEvaluator.Resolve(config.Spatial, @"c:\other");

            Assert.IsFalse(a.SpatialAllowed, "越界路径应拒绝");
            Assert.IsNotNull(a.SpatialDenyReason);
            Assert.IsTrue(a.EffectiveRoot!.StartsWith(@"d:\repo"), "应回退安全根");
        }

        [TestMethod]
        public void SpatialEvaluator_命中禁用路径_应拒绝()
        {
            var config = BoundaryConfig.Default;
            config.Spatial.BaseRoot = @"d:\repo";
            config.Spatial.AllowedRoots = new() { @"d:\repo" };
            config.Spatial.DenyPaths = new() { "\\.git" };

            var a = SpatialBoundaryEvaluator.Resolve(config.Spatial, @"d:\repo\.git\config");

            Assert.IsFalse(a.SpatialAllowed, "命中禁用路径应拒绝");
            Assert.IsNotNull(a.SpatialDenyReason);
        }

        [TestMethod]
        public void Reconstructor_compact动作_应产出压缩提示并记录状态()
        {
            var current = BoundaryContext.Initial(@"d:\repo");
            var assessment = new BoundaryAssessment(true, 0.9, 1.0, 0.0, 0.0,
                BoundaryActionKind.Compact, "上下文压力高", @"d:\repo", true, null, 0.9);

            var result = BoundaryReconstructor.Apply(current, assessment);

            Assert.IsTrue(result.Hint.Contains("压缩"), "compact 提示应含压缩语义");
            Assert.AreEqual(BoundaryActionKind.Compact, result.NextContext.LastAction);
            Assert.AreEqual(1, result.NextContext.ReconstructCount);
            Assert.AreEqual(0.9, result.NextContext.PhiOld);
        }

        [TestMethod]
        public void ContextEstimator_无真实Token_应派生估算()
        {
            var signal = HealthySignal();
            var e = ContextEstimator.Estimate(signal, baseChars: 1000);

            Assert.IsTrue(e.EstimatedChars > 0, "应派生非零估算字符数");
            Assert.IsTrue(e.Tokens.HasValue, "应派生估算 token 数");
        }

        [TestMethod]
        public void ContextEstimator_有真实Token_应优先真实值()
        {
            var signal = HealthySignal() with { InputTokens = 1234 };
            var e = ContextEstimator.Estimate(signal, baseChars: 0);

            Assert.AreEqual(1234, e.Tokens, "有真实 token 应透传真实值");
        }
    }
}
