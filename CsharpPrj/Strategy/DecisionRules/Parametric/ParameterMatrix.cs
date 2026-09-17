using System;
using System.Collections.Generic;
using BasicMethod.Declarative.Spec;

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
        /// 定义输出参数 <paramref name="output"/> 的一行：<c>out = Bias + Σ weight[i]·Get(role_i, axisId_i)</c>。
        /// 引用取规范坐标 <c>(role, axisId)</c>；裸字符串按 state → event → 常量 "1" 隐式解析（同名跨 role 时须显式带 role）。
        /// <b>装载期闸A</b>：行内每个引用必须已在 <see cref="ParameterSchema"/> 声明，否则此处即抛（不再运行期静默计 0）。
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
            foreach (var (declaredRole, src, w) in row.Cells)
            {
                var (role, axisId) = ResolveAxis(declaredRole, src);
                cells.Add(new Cell(role, axisId, w));
            }
            _rows.Add((spec, row.BiasValue, cells));
            return this;
        }

        /// <summary>
        /// 装载期坐标解析（闸A）：显式 role 引用直接校验存在性；裸名按声明数解析（0 未声明 / 1 隐式 / &gt;1 歧义），
        /// 三种失败均在此抛 <see cref="ArgumentException"/>。
        /// </summary>
        private (AxisRole Role, string AxisId) ResolveAxis(AxisRole? declaredRole, string source)
        {
            if (declaredRole is { } role)
            {
                if (_schema.TryGetAxis(role, source) is null)
                    throw new ArgumentException($"矩阵行引用了未声明的坐标轴：({role},{source})。");
                return (role, source);
            }

            var count = _schema.RoleCountOf(source);
            if (count == 0)
                throw new ArgumentException(
                    $"矩阵行引用了未声明的坐标轴：'{source}'（参数名 / event 轴 / 常量 \"1\" 必须已在 ParameterSchema 声明）。");
            if (count > 1)
                throw new ArgumentException(
                    $"矩阵引用 '{source}' 在多个 role 下均有声明，存在歧义 —— 请显式指定 AxisRole（如 Plus(AxisRole.Event, \"{source}\", w)）。");

            if (_schema.TryGetAxis(AxisRole.State, source) is not null) return (AxisRole.State, source);
            if (_schema.TryGetAxis(AxisRole.Event, source) is not null) return (AxisRole.Event, source);
            return (AxisRole.Const, source);
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
                    var v = Resolve(cell, input);
                    if (v is not null) acc += cell.Weight * v.Value;
                }
                writes.Add(ParameterWrite.Engine(spec.Name, Quantize(spec, acc)));
            }
            return writes;
        }

        /// <summary>
        /// 按规范坐标读值：state → 当前参数态；event → 该步 Variables 经所属轴的 <see cref="ParameterSpec.Coerce"/> 换算并
        /// <see cref="ParameterSpec.Clamp"/> 到值域（闸B）；const → 常量 1.0。缺省（该步无此事件）返回 null（不贡献）。
        /// </summary>
        private double? Resolve(Cell cell, ParametricStepInput input)
        {
            switch (cell.Role)
            {
                case AxisRole.State:
                    return input.State.TryGet(cell.AxisId, out var p) ? p : null;
                case AxisRole.Event:
                    var raw = input.Variable(cell.AxisId);
                    if (raw is null) return null;
                    var spec = _schema.GetAxis(AxisRole.Event, cell.AxisId);
                    return spec.Clamp(spec.Coerce(raw));
                case AxisRole.Const:
                    return 1.0;
                default:
                    return null;
            }
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

        private sealed record Cell(AxisRole Role, string AxisId, double Weight);
    }

    /// <summary>
    /// 矩阵单行的流式装配器：<c>out = Bias + Σ weight[i]·Get(role_i, axisId_i)</c>。
    /// 由 <see cref="ParameterMatrix.Row"/> 创建并装配，装配器本身不参与矩阵计算（纯声明式构建）；
    /// 引用以规范坐标 <c>(role, axisId)</c> 声明（裸名由 <see cref="ParameterMatrix.Row"/> 装载期隐式解析）。
    /// </summary>
    public sealed class ParameterMatrixRow
    {
        private double _bias;
        private readonly List<(AxisRole? Role, string AxisId, double Weight)> _cells = new();

        internal ParameterMatrixRow() { }

        /// <summary>加一个裸名源贡献（隐式 role：state → event → 常量 "1"；同名跨 role 时由装载期报歧义）；权重 0 忽略。</summary>
        public ParameterMatrixRow Plus(string source, double weight)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("矩阵输入源不允许空白。", nameof(source));
            if (weight != 0) _cells.Add((null, source, weight));
            return this;
        }

        /// <summary>按规范坐标 <c>(role, axisId)</c> 加一个源贡献（消歧义、可审计）；权重 0 忽略。</summary>
        public ParameterMatrixRow Plus(AxisRole role, string axisId, double weight)
        {
            if (string.IsNullOrWhiteSpace(axisId)) throw new ArgumentException("矩阵输入轴 Id 不允许空白。", nameof(axisId));
            if (weight != 0) _cells.Add((role, axisId, weight));
            return this;
        }

        /// <summary>保留/承接某参数原值（裸名）：<c>out += weight·axis</c>（如进度承接自身当前值）。</summary>
        public ParameterMatrixRow Carry(string param, double weight = 1.0)
            => Plus(param, weight);

        /// <summary>保留/承接某轴原值（规范坐标）：<c>out += weight·Get(role, axisId)</c>。</summary>
        public ParameterMatrixRow Carry(AxisRole role, string axisId, double weight = 1.0)
            => Plus(role, axisId, weight);

        /// <summary>常量偏置（不依赖输入，整行平移）。</summary>
        public ParameterMatrixRow Bias(double value)
        {
            _bias += value;
            return this;
        }

        internal double BiasValue => _bias;
        internal IReadOnlyList<(AxisRole? Role, string AxisId, double Weight)> Cells => _cells;
    }
}

