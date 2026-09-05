
using Agent.Calculate.Numbers;
using Agent.Calculate.Pressure;
using Agent.Contract.Boundaries;
using Agent.Core.Boundaries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 计算内核 BoundaryPressureCalculator 纯函数单测（回归锚）。
    /// 验证手写加权 Score = Σ(wᵢ·pᵢ) 与"加权平均"数学定义一致，为后续将手写加权
    /// 替换为 Mathematics 底座 Maths.Utility.WeightedAverage 提供回归基线（SingleImplementation）。
    /// 注：测试内用本地 oracle 实现加权平均，避免引入对 Mathematics 程序集的直接类型引用
    /// （Plate.dll 已内嵌 Mathematics 源码，与 Mathematics.dll 存在同名类型 CS0433 冲突）。
    /// </summary>
    [TestClass]
    [TestCategory("BoundaryFramework")]
    public class BoundaryPressureCalculatorTests
    {
        /// <summary>加权平均 oracle（Σ(vᵢ·wᵢ)/Σwᵢ）——与 Mathematics 底座 WeightedAverage 同义。</summary>
        private static double WeightedAverage(IReadOnlyList<double> values, IReadOnlyList<double> weights)
        {
            double sum = 0, total = 0;
            for (int i = 0; i < values.Count; i++) { sum += values[i] * weights[i]; total += weights[i]; }
            return Math.Abs(total) < 1e-12 ? 0 : sum / total;
        }

        private static BoundarySignal Signal(int round = 2, int resultLength = 1000, int messageCount = 5)
            => new(round, messageCount, resultLength, "ok", EvalOutcome.Pass, null, 0, 0, 3);

        [TestMethod]
        public void Compute_手写加权Score_等价加权平均()
        {
            var config = BoundaryConfig.Default.Logical;
            var estimate = new ContextEstimate((long)(config.ContextLimitChars * 0.6), null);
            var signal = Signal();

            var p = BoundaryPressureCalculator.Compute(
                signal, estimate, priorResultLength: 800,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            var w = config.SignalWeights;
            var expected = WeightedAverage(
                new[] { p.ContextPressure, p.InfoGainPressure, p.ComplexityPressure },
                new[] { w.Context, w.InfoGain, w.Complexity });

            Assert.IsTrue(Math.Abs(p.Score - expected) < 1e-9,
                $"手写 Score({p.Score}) 应与加权平均({expected}) 一致");
        }

        [TestMethod]
        public void Compute_权重和为1_Score落在0到1闭区间()
        {
            var config = BoundaryConfig.Default.Logical;
            var signal = Signal(round: config.MaxRounds, resultLength: 0);

            var p = BoundaryPressureCalculator.Compute(
                signal, new ContextEstimate(config.ContextLimitChars, null), priorResultLength: 100,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.IsTrue(p.Score >= 0d && p.Score <= 1d, $"Score 应在 [0,1]，实际 {p.Score}");
            Assert.IsTrue(p.ContextPressure >= 0d && p.ContextPressure <= 1d);
            Assert.IsTrue(p.InfoGainPressure >= 0d && p.InfoGainPressure <= 1d);
            Assert.IsTrue(p.ComplexityPressure >= 0d && p.ComplexityPressure <= 1d);
        }

        [TestMethod]
        public void ContextPressureOf_与Compute上下文压力_单维一致()
        {
            var config = BoundaryConfig.Default.Logical;
            var estimate = new ContextEstimate((long)(config.ContextLimitChars * 0.5), null);

            var single = BoundaryPressureCalculator.ContextPressureOf(
                estimate, config.ContextLimitChars, config.ContextWatermark);
            var p = BoundaryPressureCalculator.Compute(
                Signal(), estimate, priorResultLength: 800,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.AreEqual(single, p.ContextPressure, 1e-9,
                "ContextPressureOf 单维门控应与 Compute 内上下文压力一致（单一实现）");
        }

        // ---- D2 ContextPressure 迟滞（防 compact 后打转） ----

        private static ContextEstimate FullContext(BoundaryConfig config)
            => new(config.Logical.ContextLimitChars, null);

        [TestMethod]
        public void LogicalEvaluator_重构后同高压_迟滞抑制再次触发()
        {
            var config = BoundaryConfig.Default;
            var signal = Signal(round: 2);

            // 首次：无迟滞，高压 → 触发
            var first = LogicalBoundaryEvaluator.Evaluate(signal, config, FullContext(config), priorResultLength: 800);
            Assert.IsTrue(first.Triggered, "无迟滞时高压应触发");
            Assert.IsFalse(first.HysteresisHeld);

            // 重构后下一轮（wasTriggered=true）：同高压 → 迟滞保持，抑制再次触发
            var second = LogicalBoundaryEvaluator.Evaluate(signal, config, FullContext(config), priorResultLength: 800, wasTriggered: true);
            Assert.IsTrue(second.HysteresisHeld, "重构后高压应处于迟滞保持");
            Assert.IsFalse(second.Triggered, "迟滞保持期间不应再次触发（防打转）");
        }

        [TestMethod]
        public void LogicalEvaluator_重构后压力回落_解除迟滞()
        {
            var config = BoundaryConfig.Default;
            var signal = Signal(round: 2);

            // 重构后（wasTriggered=true）但上下文已回落 → 迟滞解除、不再保持
            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, ContextEstimate.Empty, priorResultLength: 800, wasTriggered: true);
            Assert.IsFalse(a.HysteresisHeld, "Score 回落到复位阈值以下应解除迟滞");
            Assert.IsFalse(a.Triggered, "压力回落不应触发");
        }

        [TestMethod]
        public void LogicalEvaluator_强信号_不受迟滞抑制()
        {
            var config = BoundaryConfig.Default;
            var signal = Signal(round: 2) with { StopReason = "model_context_window_exceeded" };

            // 即使处于迟滞（wasTriggered=true、Score 未回落），协议强信号仍应短路触发
            var a = LogicalBoundaryEvaluator.Evaluate(signal, config, FullContext(config), priorResultLength: 800, wasTriggered: true);
            Assert.IsTrue(a.Triggered, "协议硬断应短路触发，不受迟滞抑制");
            Assert.AreEqual(BoundaryActionKind.Compact, a.Action);
        }

        // ---- B3 场景补齐（防误算，消隐不新增计算） ----

        [TestMethod]
        public void Compute_首轮空结果_不产生增益惩罚()
        {
            var config = BoundaryConfig.Default.Logical;
            // 首轮：prior=0，结果为空 → rate 显式默认 1.0，不应判"增益趋零"
            var signal = Signal(round: 1, resultLength: 0);

            var p = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 0,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.AreEqual(1.0, p.InfoGainRate, 1e-9, "首轮空结果 rate 应显式默认 1.0");
            Assert.IsTrue(p.InfoGainPressure < 0.5, "首轮空结果不应判为平台期（信息增益无惩罚）");
        }

        [TestMethod]
        public void Compute_重复空结果_允许熔断触发()
        {
            var config = BoundaryConfig.Default.Logical;
            // 重复空结果：prior>0 且结果为空 → rate=0 ≤ 下限 → 信息增益压力打满（RepeatedCallStable 熔断）
            var signal = Signal(round: 3, resultLength: 0);

            var p = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 500,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.AreEqual(1.0, p.InfoGainPressure, 1e-9, "重复空结果应判增益趋零（允许熔断）");
        }

        [TestMethod]
        public void Compute_TokenMax未配置_不强制打满复杂度()
        {
            var config = BoundaryConfig.Default.Logical;
            // TokenMax=0（未配置上限）：即使 Eval≠Pass 也不应打满复杂度（退化，不污染总分）
            var signal = Signal(round: 1, resultLength: 100) with { Eval = EvalOutcome.Partial, TokenValue = 3, TokenMax = 0 };

            var p = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 100,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.IsTrue(p.ComplexityPressure < 1.0, "TokenMax=0 时不应打满复杂度压力");
        }

        // ---- D4 累计无进展步数（消费闲置配置 ComplexityMaxSteps） ----

        [TestMethod]
        public void Compute_启用无进展权重_复杂度压力随步数抬升()
        {
            var config = BoundaryConfig.Default.Logical;
            // 启用深度项：Complexity=0.1（轮次占比）+ ComplexitySteps=0.1（无进展占比）
            var weights = new BoundaryWeights { Context = 0.5, InfoGain = 0.3, Complexity = 0.1, ComplexitySteps = 0.1 };
            var signal = Signal(round: 1, resultLength: 1000) with { NoProgressSteps = 10 };

            var p = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 900,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: weights,
                complexityMaxSteps: 25);

            double roundOnly = 1d / config.MaxRounds;              // ≈ 0.333
            // ComplexitySteps 为深度项真实权重（默认 0=不启用；1.0 等价全量计入）——实现按权重缩放 steps 占比
            double expected = roundOnly + weights.ComplexitySteps * (10d / 25d);   // ≈ 0.373（轮次占比 + 无进展占比×权重 0.1）
            Assert.AreEqual(expected, p.ComplexityPressure, 1e-9,
                $"复杂度压力应含无进展占比（轮次 {roundOnly:0.00} + 无进展 0.40×{weights.ComplexitySteps}）");
        }

        [TestMethod]
        public void Compute_未启用无进展权重_步数不改变复杂度()
        {
            var config = BoundaryConfig.Default.Logical;
            // 默认权重 ComplexitySteps=0 → 深度项不生效，行为与现状一致（向后兼容）
            var signal = Signal(round: 1, resultLength: 1000) with { NoProgressSteps = 10 };

            var p = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 900,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights,
                complexityMaxSteps: 25);

            Assert.AreEqual(1d / config.MaxRounds, p.ComplexityPressure, 1e-9,
                "未启用无进展权重时复杂度压力应仅含轮次占比");
        }

        // ---- D3 rate_t EWMA 平滑速率（InfoGainSmoothing + Compute 消费） ----

        [TestMethod]
        public void InfoGainSmoothing_平滑单轮抖动()
        {
            // 窗口（旧→新）：末轮骤降到 500；单步 rate=0.5，EWMA 平滑后应更高（抗抖）
            var smoothed = InfoGainSmoothing.Ewma(new[] { 1000, 1000, 1000, 500 }, alpha: 0.5);

            Assert.IsNotNull(smoothed);
            Assert.IsTrue(smoothed > 0.5 && smoothed < 1.0,
                $"EWMA 平滑速率应介于单步(0.5)与 1.0 之间，实际 {smoothed:0.000}");
            // 期望值：rates=0.5/1.0/1.0，权重 0.5/0.25/0.125 → 0.7143
            Assert.AreEqual(0.7143, smoothed!.Value, 1e-3, "EWMA 加权平均结果应约 0.7143");
        }

        [TestMethod]
        public void InfoGainSmoothing_不足两段_返回null()
        {
            Assert.IsNull(InfoGainSmoothing.Ewma(new[] { 1000 }, 0.5), "单长度无速率应返回 null（回退单步）");
        }

        [TestMethod]
        public void Compute_消费平滑速率_抗单轮抖动()
        {
            var config = BoundaryConfig.Default.Logical;
            var signal = Signal(round: 2, resultLength: 500);
            // 单步 rate=500/5000=0.1（远低于 minRate 0.05 附近 → 惩罚重）；平滑速率 0.6 → 惩罚轻
            var withSmoothing = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 5000,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights,
                smoothedRate: 0.6);
            var noSmoothing = BoundaryPressureCalculator.Compute(
                signal, ContextEstimate.Empty, priorResultLength: 5000,
                contextLimitChars: config.ContextLimitChars,
                contextWatermark: config.ContextWatermark,
                infoGainMinRate: config.InfoGainMinRate,
                maxRounds: config.MaxRounds,
                weights: config.SignalWeights);

            Assert.AreEqual(0.6, withSmoothing.InfoGainRate, 1e-9, "输出速率应等于平滑速率");
            Assert.IsTrue(withSmoothing.InfoGainPressure < noSmoothing.InfoGainPressure,
                $"平滑速率应降低信息增益压力（单轮抖动不误判），平滑 {withSmoothing.InfoGainPressure:0.00} < 单步 {noSmoothing.InfoGainPressure:0.00}");
        }
    }
}
