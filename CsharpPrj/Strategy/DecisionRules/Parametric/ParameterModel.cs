using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 参数值域。所有决策变量经规则归约后都以 <see cref="double"/> 编码承载在参数态里，便于直接数值/序数计算：
    /// <list type="bullet">
    /// <item><see cref="Flag"/>：二值（0/1），归约自布尔语义（如 halt 位、是否落库）。</item>
    /// <item><see cref="Ordinal"/>：有界序数（如 FailureBudget.level 0..3、ProgressToken.value）。</item>
    /// <item><see cref="Numeric"/>：实数（含连续/可测度量，如归一的进度 [0,1]、Quality）。</item>
    /// </list>
    /// 语义类型轴见 clinerules axioms.md（ProgressToken/FailureBudget/ControlFlag 参数化承载）。
    /// </summary>
    public enum ParameterKind
    {
        Flag = 0,
        Ordinal = 1,
        Numeric = 2,
    }

    /// <summary>
    /// 参数定义（一个可判定分量）：名称 + 值域 + 边界 + <see cref="IsForced"/> + <see cref="IsLatched"/>。
    /// <para><see cref="IsForced"/> = true 表示「确定性强制分量」（引擎/确定性核心单一写入，如预算/进度/halt），
    /// 禁止被输入归约/外部策略供给写入 —— 对齐 clinerules SEP.DeterministicCoreDisjoint（写路径单一）。</para>
    /// <para><see cref="IsLatched"/> = true 表示「吸收/闩锁触发分量」（复合矩阵运算定律 A）：该分量在
    /// 一次运行内只允许单调不降——一旦到达某触发值即成为吸收态，后续任何来源（含 Engine）的低值写都会被忽略
    /// （见 <see cref="ParameterState.Apply"/>）。用于 halt 这类"一旦置真绝不被后续复合步翻盘"的终值主控位。</para>
    /// </summary>
    public sealed record ParameterSpec(
        string Name,
        ParameterKind Kind = ParameterKind.Numeric,
        double Default = 0.0,
        bool IsForced = false,
        double Min = double.NegativeInfinity,
        double Max = double.PositiveInfinity,
        string? Description = null,
        bool IsLatched = false)
    {
        /// <summary>Flag 值域缺省 [0,1]；Ordinal/Numeric 缺省无界。</summary>
        public double Clamp(double value)
        {
            var effectiveMin = Kind == ParameterKind.Flag && double.IsNegativeInfinity(Min) ? 0.0 : Min;
            var effectiveMax = Kind == ParameterKind.Flag && double.IsPositiveInfinity(Max) ? 1.0 : Max;
            return Math.Clamp(value, effectiveMin, effectiveMax);
        }

        /// <summary>把语义原始值按参数值域归约为数值参数（Flag:bool→0/1；数/数值串→double；其余→Default）。</summary>
        public double Coerce(object? raw)
            => raw switch
            {
                null => Default,
                bool b => Kind == ParameterKind.Flag ? (b ? 1.0 : 0.0) : Default,
                int i => Clamp(i),
                long l => Clamp(l),
                float f => Clamp(f),
                double d => Clamp(d),
                decimal m => Clamp((double)m),
                string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) => Clamp(v),
                string sb when bool.TryParse(sb, out var bv) => bv ? 1.0 : 0.0,
                _ => Default,
            };
    }

    /// <summary>
    /// 参数 schema：一组 <see cref="ParameterSpec"/> 的有序集合（唯一名），是「参数态」的坐标系定义。
    /// 一份 schema 同时驱动：①变量→参数归约、②内层参数演进、③参数→标签解码（JSON 聚拢点在参数域的对偶）。
    /// </summary>
    public sealed class ParameterSchema
    {
        private readonly IReadOnlyDictionary<string, ParameterSpec> _byName;
        private readonly IReadOnlyList<ParameterSpec> _ordered;

        public ParameterSchema(IEnumerable<ParameterSpec> specs)
        {
            if (specs is null) throw new ArgumentNullException(nameof(specs));
            _ordered = specs.ToList();
            var map = new Dictionary<string, ParameterSpec>(StringComparer.Ordinal);
            foreach (var spec in _ordered)
            {
                if (spec is null || string.IsNullOrWhiteSpace(spec.Name))
                    throw new ArgumentException("参数定义不允许 null/空白名。", nameof(specs));
                if (!map.TryAdd(spec.Name, spec))
                    throw new ArgumentException($"参数名重复：{spec.Name}", nameof(specs));
            }
            _byName = map;
        }

        /// <summary>有序全量定义。</summary>
        public IReadOnlyList<ParameterSpec> Specs => _ordered;

        /// <summary>按名查找；未定义返回 null。</summary>
        public ParameterSpec? TryGet(string name)
            => !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var s) ? s : null;

        /// <summary>按名取定义；未定义抛 <see cref="KeyNotFoundException"/>。</summary>
        public ParameterSpec Get(string name)
            => TryGet(name) ?? throw new KeyNotFoundException($"未定义的参数：{name}");

        /// <summary>该参数是否为确定性强制分量。</summary>
        public bool IsForced(string name) => TryGet(name) is { IsForced: true };

        /// <summary>强制分量集合（确定性核心写路径的允许集）。</summary>
        public IReadOnlyList<ParameterSpec> Forced => _ordered.Where(s => s.IsForced).ToList();

        /// <summary>构建默认参数态（各分量取 Default）。</summary>
        public ParameterState CreateDefault() => ParameterState.CreateDefault(this);
    }
}
