using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 语义规则到线性判别向量的编译器。语义以"可读规则"声明（决策 C 需要词元集 AND 命中），
    /// 编译产物是 <see cref="SemanticVocabulary"/> 正交基上的判别向量（权重行）。
    /// 权重由规则自动导出，避免手写魔法数：对每条决策所需词元给权重 +1，并置 <c>bias = -(n-0.5)</c>，
    /// 使仅当 n 个必需词元**全部**命中时得分 ≥ 0.5（AND 语义被转成线性可分的超平面）。
    /// </summary>
    /// <remarks>
    /// 复合矩阵运算基础定律 D（字典序优先 / 显式支配）：互斥裁决的胜出由<b>显式优先级</b>（数值越小越优先）决定，
    /// 而非注册顺序的副作用。默认优先级 = 注册序（向后兼容）；主控/终值类（如 halt→TM、progress触顶→TO、intent_fail）
    /// 应显式给更低优先级值，使其即使与从属类同时命中也能确定胜出 —— 杜绝"装配偶然决定语义 / 注册序误支配"。
    /// </remarks>
    public sealed class SemanticClassRules
    {
        private readonly SemanticVocabulary _vocab;
        private readonly List<Class> _classes = new();

        /// <summary>一条已注册决策类。Order 为注册序：同优先级时保持"先注册者优先"的稳定回退。</summary>
        private sealed record Class(string Label, int Priority, int Order, double[] W, double Bias, SemanticTerm[] Required);

        public SemanticClassRules(SemanticVocabulary vocab) => _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));

        /// <summary>
        /// 注册一类决策：<paramref name="label"/> 当且仅当 <paramref name="required"/>（AND）全部命中时成立。
        /// 判别向量权重 = 所需词元逐一置 1；bias = -(count-0.5) 实现 AND 阈值门。
        /// 优先级 = 注册序（最早注册最优先，向后兼容）；需显式支配用带 <paramref name="priority"/> 的重载。
        /// </summary>
        public SemanticClassRules WhenAllOf(string label, params SemanticTerm[] required)
            => Register(label, _classes.Count, required);

        /// <summary>
        /// 注册一类决策并显式给定优先级（数值越小越优先）：主控/终值类给低值，使它们在
        /// <see cref="ClassifyExclusive"/> 中确定性压过从属类 —— 支配是命名属性，不依赖注册顺序。
        /// </summary>
        public SemanticClassRules WhenAllOf(string label, int priority, params SemanticTerm[] required)
        {
            if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority), "优先级不允许为负。");
            return Register(label, priority, required);
        }

        private SemanticClassRules Register(string label, int priority, SemanticTerm[] required)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("决策标签不允许空白。", nameof(label));
            if (required is null || required.Length == 0) throw new ArgumentException("至少需要一个必需词元。", nameof(required));

            var w = new double[_vocab.Dimension];
            foreach (var t in required)
            {
                var i = _vocab.IndexOf(t);
                if (i < 0) throw new ArgumentException($"词元不在词汇表内：{t}");
                w[i] += 1.0;
            }
            var bias = -(required.Length - 0.5);
            _classes.Add(new Class(label, priority, _classes.Count, w, bias, required.ToArray()));
            return this;
        }

        /// <summary>
        /// 对激活词元集做裁决：返回**全部命中**（得分 ≥ 0.5）的类集合（按注册序；不受优先级过滤，
        /// 供审计/归因读取全体命中）。若希望互斥（只取一个），用 <see cref="ClassifyExclusive"/>。
        /// </summary>
        public IReadOnlyList<(string Label, double Score)> Classify(IEnumerable<SemanticTerm> active)
        {
            var x = _vocab.Embed(active);
            var hits = new List<(string Label, double Score)>();
            foreach (var c in _classes)
            {
                var score = Score(c, x);
                if (score >= 0.5) hits.Add((c.Label, score));
            }
            return hits;
        }

        /// <summary>
        /// 互斥裁决：按「显式优先级(越小越优) → 注册序」取最优命中类；无命中返回 null（显式非绿/兜底由调用方给默认）。
        /// 支配是显式属性而非注册顺序副作用 —— 主控/终值类即使后注册也能确定性胜出。
        /// </summary>
        public string? ClassifyExclusive(IEnumerable<SemanticTerm> active)
        {
            var x = _vocab.Embed(active);
            Class? best = null;
            foreach (var c in _classes)
            {
                if (Score(c, x) < 0.5) continue;
                if (best is null || c.Priority < best.Priority) best = c; // 同优先级保留先注册者
            }
            return best?.Label;
        }

        private static double Score(Class c, double[] x)
        {
            var score = c.Bias;
            for (var i = 0; i < c.W.Length; i++) if (c.W[i] != 0) score += c.W[i] * x[i];
            return score;
        }

        /// <summary>已注册的决策类数。</summary>
        public int ClassCount => _classes.Count;
    }
}
