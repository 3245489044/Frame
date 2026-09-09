using System;
using System.Collections.Generic;
using System.Globalization;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 参数矩阵（内层真正的多维矩阵计算载体）：把「参数态 + 本步事件」作为**输入向量**，
    /// 对一个 <c>权重矩阵 W × 输入 + 偏置 b</c> 做整向量变换，再按各输出参数的 <see cref="ParameterKind"/>
    /// **逐轴离散量化**（Flag 阈值门 / Ordinal 取整收敛 / Numeric 边界收敛），得到下一参数态。
    /// 与传统"线性逐分量 if/写"不同：所有输出并行由同一矩阵 <c>W·x+b</c> 一次性算出，
    /// 对标真实多维矩阵仿射 + 逐轴激活层的计算形态（消除散落的标量顺序写）。
    /// </summary>
    /// <remarks>
    /// 来源语义：本矩阵（Engine）是唯一可触碰 <see cref="ParameterSpec.IsForced"/> 分量的写来源，
    /// 与 <see cref="ParameterState.Apply"/> 的守卫（Reduce/Policy 写强制被拒）构成 DeterministicCoreDisjoint 双重防线。
    /// LLM-free：系数由调用方（规则/导演适配层）构造注入，本类只做 <c>W·x+b</c> + 量化。
    /// </remarks>
    public sealed class ParameterMatrix
    {
        private readonly ParameterSchema _schema;
        private readonly List<(ParameterSpec Output, double Bias, IReadOnlyList<Cell> Cells)> _rows = new();

        private ParameterMatrix(ParameterSchema schema) => _schema = schema;

        /// <summary>绑定到给定参数坐标系构造一个空矩阵。</summary>
        public static ParameterMatrix For(ParameterSchema schema)
            => new(schema ?? throw new ArgumentNullException(nameof(schema)));

        /// <summary>已配置的输出行数（便于断言/自检）。</summary>
        public int RowCount => _rows.Count;

        /// <summary>
        /// 定义输出参数 <paramref name="output"/> 的一行：<c>out = Bias + Σ weight[i]·source[i]</c>。
        /// <paramref name="source"/> 取：参数名（当前参数态）、事件键（该步 Variables）、常量 "1"。
        /// 配置后用 <see cref="ApplyTo"/> 对整向量一次性执行。
        /// </summary>
        public ParameterMatrix Row(string output, Action<ParameterMatrixRow> configure)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            var spec = _schema.TryGet(output)
                ?? throw new ArgumentException($"未定义的输出参数：{output}", nameof(output));

            var row = new ParameterMatrixRow();
            configure(row);
            var cells = new List<Cell>(row.Cells.Count);
            foreach (var (src, w) in row.Cells) cells.Add(new Cell(src, w));
            _rows.Add((spec, row.BiasValue, cells));
            return this;
        }

        /// <summary>
        /// 对给定步输入执行整向量矩阵变换：<c>next[out] = Quantize( Σ W·x + b )</c>，产出 <see cref="ParameterWriteSource.Engine"/> 写。
        /// 一次调用即完成该步全部内层参数演进（整向量并行计算，非逐条顺序 if）。
        /// </summary>
        public IReadOnlyList<ParameterWrite> ApplyTo(ParametricStepInput input)
        {
            if (input.State.Schema != _schema)
                throw new InvalidOperationException("矩阵与输入参数态坐标系不一致。");

            var writes = new List<ParameterWrite>(_rows.Count);
            foreach (var (spec, bias, cells) in _rows)
            {
                var acc = bias;
                foreach (var cell in cells)
                {
                    var v = Resolve(cell.Source, input);
                    if (v is not null) acc += cell.Weight * v.Value;
                }
                writes.Add(ParameterWrite.Engine(spec.Name, Quantize(spec, acc)));
            }
            return writes;
        }

        /// <summary>解析矩阵行引用：参数名 → 当前参数值；"1" → 常量；否则按事件键读该步 Variables；未知 → null（计 0 贡献）。</summary>
        private static double? Resolve(string source, ParametricStepInput input)
        {
            if (input.State.TryGet(source, out var p)) return p;
            if (source == "1") return 1.0;
            return input.Variable(source) is { } raw ? AsDouble(raw) : null;
        }

        /// <summary>把矩阵连续值按输出参数值域离散量化：Flag 阈值门 / Ordinal 取整 + 边界 / Numeric 边界。</summary>
        private static double Quantize(ParameterSpec spec, double raw)
        {
            var clamped = spec.Clamp(raw);
            return spec.Kind switch
            {
                ParameterKind.Flag => clamped >= 0.5 ? 1.0 : 0.0,
                ParameterKind.Ordinal => spec.Clamp(Math.Round(clamped)),
                _ => clamped,
            };
        }

        /// <summary>把事件原始值换算为数值贡献（布尔 / 数 / 数值串）。</summary>
        private static double? AsDouble(object? raw)
            => raw switch
            {
                double d => d,
                int i => i,
                long l => l,
                float f => f,
                decimal m => (double)m,
                bool b => b ? 1.0 : 0.0,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
                string sb when bool.TryParse(sb, out var bv) => bv ? 1.0 : 0.0,
                _ => null,
            };

        private sealed record Cell(string Source, double Weight);
    }

    /// <summary>
    /// 矩阵单行的流式装配器：<c>out = Bias + Σ weight[i]·source[i]</c>。
    /// 由 <see cref="ParameterMatrix.Row"/> 创建并装配，装配器本身不参与矩阵计算（纯声明式构建）。
    /// </summary>
    public sealed class ParameterMatrixRow
    {
        private double _bias;
        private readonly List<(string Source, double Weight)> _cells = new();

        internal ParameterMatrixRow() { }

        /// <summary>加一个源贡献：<paramref name="source"/> 为参数名 / 事件键 / 常量 "1"；权重 0 忽略。</summary>
        public ParameterMatrixRow Plus(string source, double weight)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("矩阵输入源不允许空白。", nameof(source));
            if (weight != 0) _cells.Add((source, weight));
            return this;
        }

        /// <summary>保留/承接某参数原值：<c>out += weight·param</c>（如进度承接自身当前值）。</summary>
        public ParameterMatrixRow Carry(string param, double weight = 1.0)
            => Plus(param, weight);

        /// <summary>常量偏置（不依赖输入，整行平移）。</summary>
        public ParameterMatrixRow Bias(double value)
        {
            _bias += value;
            return this;
        }

        internal double BiasValue => _bias;
        internal IReadOnlyList<(string Source, double Weight)> Cells => _cells;
    }
}

