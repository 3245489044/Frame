using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 语义词元：一个"域(轴) × 取值"。命名承载语义（如 Axis=eval, Term=pass），是人话与向量之间的锚点。
    /// 轴的概念对齐 clinerules axioms（轴 = 正交独立关注面；词元 = 该轴上的可判定取值，如 eval∈{pass,partial,fail}）。
    /// </summary>
    public readonly record struct SemanticTerm(string Axis, string Term)
    {
        public override string ToString() => $"{Axis}={Term}";
    }

    /// <summary>
    /// 有限语义词汇表：封闭域的单一真源。给出该域可枚举的"轴 → 词元集"，
    /// 并为每个词元分配一个**正交完备基向量**（one-hot 轴；不同轴/词元互不正交即语义不混淆），
    /// 使任何有限词元子集都可被唯一地嵌入成一个向量、且可被无失真采样还原（embed∘sample = id）。
    /// 这就是"用定义好的固定向量取代真实语义"：语义宇宙有限 ⇒ 该基是完备表示，无需学习/分布语义。
    /// </summary>
    public sealed class SemanticVocabulary
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _axisTerms;
        private readonly IReadOnlyDictionary<SemanticTerm, int> _index;
        private readonly IReadOnlyList<SemanticTerm> _ordered;

        /// <param name="axisTerms">轴 → 该轴可枚举词元（保持声明顺序，顺序即索引）。</param>
        public SemanticVocabulary(IReadOnlyDictionary<string, IReadOnlyList<string>> axisTerms)
        {
            _axisTerms = axisTerms ?? throw new ArgumentNullException(nameof(axisTerms));
            if (_axisTerms.Count == 0) throw new ArgumentException("词汇表不能为空。", nameof(axisTerms));

            var ordered = new List<SemanticTerm>();
            var index = new Dictionary<SemanticTerm, int>();
            foreach (var (axis, terms) in _axisTerms)
            {
                if (string.IsNullOrWhiteSpace(axis)) throw new ArgumentException("轴不允许空白。");
                if (terms is null || terms.Count == 0) throw new ArgumentException($"轴 {axis} 至少需要一个词元。");
                foreach (var term in terms)
                {
                    if (string.IsNullOrWhiteSpace(term)) throw new ArgumentException($"轴 {axis} 的词元不允许空白。");
                    var t = new SemanticTerm(axis, term);
                    if (!index.TryAdd(t, ordered.Count))
                        throw new ArgumentException($"词元重复：{t}。");
                    ordered.Add(t);
                }
            }
            _ordered = ordered;
            _index = index;
        }

        /// <summary>全部词元（索引即向量维度位；维度数 = 词元总数）。</summary>
        public IReadOnlyList<SemanticTerm> Terms => _ordered;

        /// <summary>向量维度数（= 词元总数）。</summary>
        public int Dimension => _ordered.Count;

        /// <summary>轴清单。</summary>
        public IReadOnlyCollection<string> Axes => _axisTerms.Keys.ToList();

        /// <summary>某轴的可枚举词元。</summary>
        public IReadOnlyList<string> TermsOf(string axis)
            => _axisTerms.TryGetValue(axis, out var t) ? t : Array.Empty<string>();

        /// <summary>词元 → 向量维度位（未定义返回 -1）。</summary>
        public int IndexOf(SemanticTerm term) => _index.TryGetValue(term, out var i) ? i : -1;

        /// <summary>轴 + 取值 → 词元；该轴未定义该取值返回 null。</summary>
        public SemanticTerm? Term(string axis, string term)
        {
            var t = new SemanticTerm(axis, term);
            return _index.ContainsKey(t) ? t : null;
        }

        /// <summary>把一组激活词元编码为指示向量（one-hot 完备基）：命中位 1，其余 0。未知词元忽略。</summary>
        public double[] Embed(IEnumerable<SemanticTerm> active)
        {
            var vec = new double[Dimension];
            if (active is null) return vec;
            foreach (var t in active)
                if (_index.TryGetValue(t, out var i)) vec[i] = 1.0;
            return vec;
        }

        /// <summary>从编码向量还原激活词元（逆映射；sample(embed(x)) = x）。</summary>
        public IReadOnlyList<SemanticTerm> Sample(double[] encoded)
        {
            if (encoded is null) throw new ArgumentNullException(nameof(encoded));
            if (encoded.Length != Dimension)
                throw new ArgumentException($"向量维度 {encoded.Length} 与词汇维度 {Dimension} 不一致。", nameof(encoded));
            return _ordered.Where((_, i) => encoded[i] > 0).ToList();
        }
    }
}
