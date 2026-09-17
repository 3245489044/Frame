using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Spec
{
    /// <summary>
    /// 参数写来源（写路径分域，实现「确定性核心写路径单一」）：
    /// <list type="bullet">
    /// <item><see cref="Engine"/>：引擎确定性写（唯一被允许触碰 <see cref="ParameterSpec.IsForced"/> 分量的来源）。</item>
    /// <item><see cref="Reduce"/>：① 变量→参数 归约写（语义输入落位；禁止写强制分量）。</item>
    /// <item><see cref="Policy"/>：外部策略供给写（LLM/Config 的提议；禁止写强制分量）。</item>
    /// </list>
    /// </summary>
    public enum ParameterWriteSource
    {
        Engine = 0,
        Reduce = 1,
        Policy = 2,
    }

    /// <summary>对某个参数的一次写（目标名 + 目标值 + 来源）。由内核/转移算子产出后统一应用。</summary>
    public readonly record struct ParameterWrite(string Name, double Value, ParameterWriteSource Source)
    {
        public static ParameterWrite Engine(string name, double value) => new(name, value, ParameterWriteSource.Engine);
        public static ParameterWrite Reduce(string name, double value) => new(name, value, ParameterWriteSource.Reduce);
        public static ParameterWrite Policy(string name, double value) => new(name, value, ParameterWriteSource.Policy);
    }

    /// <summary>
    /// 参数态：不可变快照（<c>IReadOnlyDictionary&lt;string,double&gt;</c>），所有分量以 <see cref="double"/> 数值承载。
    /// 演进不就地改本对象，而是经 <see cref="Apply"/> 派生新态 —— 每步留痕，天然支持闭环轨迹回溯与信息单调性。
    /// </summary>
    public sealed class ParameterState
    {
        private readonly ParameterSchema _schema;
        private readonly IReadOnlyDictionary<string, double> _values;

        private ParameterState(ParameterSchema schema, IReadOnlyDictionary<string, double> values)
        {
            _schema = schema;
            _values = values;
        }

        /// <summary>坐标系（所属 schema）。</summary>
        public ParameterSchema Schema => _schema;

        /// <summary>当前值快照（只读拷贝）。</summary>
        public IReadOnlyDictionary<string, double> Values => new Dictionary<string, double>(_values);

        /// <summary>按名取当前值；未归约到的分量回退 schema Default。</summary>
        public double Get(string name)
        {
            if (_values.TryGetValue(name, out var v)) return v;
            var spec = _schema.TryGet(name);
            return spec is null ? 0.0 : spec.Clamp(spec.Default);
        }

        /// <summary>尝试取当前值。</summary>
        public bool TryGet(string name, out double value) => _values.TryGetValue(name, out value);

        /// <summary>构建默认参数态（各分量取 schema Default，含强制分量）。</summary>
        public static ParameterState CreateDefault(ParameterSchema schema)
            => new(schema, schema.Specs.ToDictionary(s => s.Name, s => s.Clamp(s.Default)));

        /// <summary>
        /// 应用一批写，派生新参数态。
        /// 守卫：<see cref="ParameterWriteSource.Engine"/> 可写任何分量（含强制）；Reduce/Policy 写强制分量会被拒绝并计入
        /// <paramref name="rejected"/>（确定性核心不被外部供给越权改写）。数值经所属 spec 的 Clamp 收敛。
        /// </summary>
        public ParameterState Apply(
            IEnumerable<ParameterWrite> writes,
            out IReadOnlyList<string> rejected)
        {
            if (writes is null) throw new ArgumentNullException(nameof(writes));
            var next = new Dictionary<string, double>(_values);
            var rej = new List<string>();
            foreach (var w in writes)
            {
                var spec = _schema.TryGet(w.Name);
                if (spec is null) { rej.Add(w.Name); continue; }
                if (w.Source != ParameterWriteSource.Engine && spec.IsForced)
                {
                    // 外部（Reduce/Policy）企图写强制分量 → 拒绝（不应用、不吞，交调用方归因）。
                    rej.Add(w.Name);
                    continue;
                }

                if (spec.IsLatched)
                {
                    // 吸收/闩锁（定律 A）：主控触发分量单调不降 —— 本步低值写被忽略，
                    // 保证"一旦置真不被后续复合步/来源(含 Engine)翻盘"；Engine 亦不能回调。
                    next[w.Name] = Math.Max(next[w.Name], spec.Clamp(w.Value));
                }
                else
                {
                    next[w.Name] = spec.Clamp(w.Value);
                }
            }
            rejected = rej;
            return new ParameterState(_schema, next);
        }

        /// <summary>便捷：逐条写派生（等价 <see cref="Apply"/> 的无拒绝消费形态）。</summary>
        public ParameterState Apply(IEnumerable<ParameterWrite> writes)
            => Apply(writes, out _);
    }
}
