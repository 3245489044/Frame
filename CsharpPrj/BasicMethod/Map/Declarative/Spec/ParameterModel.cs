using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Spec
{
    /// <summary>
    /// 参数值域。所有声明变量经归约后都以 <see cref="double"/> 编码承载在参数态里，便于直接数值/序数计算：
    /// <list type="bullet">
    /// <item><see cref="Flag"/>：二值（0/1），归约自布尔语义（如停机位、是否落库）。</item>
    /// <item><see cref="Ordinal"/>：有界序数（如等级 0..3、计数器）。</item>
    /// <item><see cref="Numeric"/>：实数（含连续/可测度量，如归一的进度 [0,1]、质量分）。</item>
    /// </list>
    /// 纯机制件：只定义值域形态，不承载任何领域语义。
    /// </summary>
    public enum ParameterKind
    {
        Flag = 0,
        Ordinal = 1,
        Numeric = 2,
    }

    /// <summary>
    /// 坐标轴演化角色（统一坐标系第一属性，独立于值类型 <see cref="ParameterKind"/>）：
    /// <list type="bullet">
    /// <item><see cref="State"/>：跨步持久，进 <see cref="ParameterState"/>（含 <see cref="ParameterSpec.IsForced"/> 强制分量）。</item>
    /// <item><see cref="Event"/>：单步瞬时，不进状态演进，只在步输入变量集中存活（须在 schema 显式声明）。</item>
    /// <item><see cref="Const"/>：内建常量轴，仅 <c>"1"</c>（不依赖输入）。</item>
    /// </list>
    /// </summary>
    public enum AxisRole
    {
        State = 0,
        Event = 1,
        Const = 2,
    }

    /// <summary>
    /// 轴定义（一个可判定分量）：轴身份 <see cref="Role"/>（state/event/const）+ 名称 + 值类型 + 值域 + 边界
    /// + <see cref="IsForced"/> + <see cref="IsLatched"/> —— 规范坐标为 <c>(Role, Name)</c>。
    /// <para><see cref="IsForced"/> = true 表示「确定性强制分量」（确定性核心单一写入，如预算/进度/停机位），
    /// 禁止被输入归约/外部策略供给写入 —— 写路径单一。</para>
    /// <para><see cref="IsLatched"/> = true 表示「吸收/闩锁触发分量」（复合矩阵运算定律 A）：该分量在
    /// 一次运行内只允许单调不降——一旦到达某触发值即成为吸收态，后续任何来源（含 Engine）的低值写都会被忽略
    /// （见 <see cref="ParameterState.Apply"/>）。用于「一旦置真绝不被后续复合步翻盘」的终值主控位。</para>
    /// </summary>
    public sealed record ParameterSpec(
        string Name,
        ParameterKind Kind = ParameterKind.Numeric,
        double Default = 0.0,
        bool IsForced = false,
        double Min = double.NegativeInfinity,
        double Max = double.PositiveInfinity,
        string? Description = null,
        bool IsLatched = false,
        AxisRole Role = AxisRole.State)
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
    /// 参数 schema：轴空间单一真源 —— state 轴集合（<see cref="Specs"/>，构成「参数态」坐标系）
    /// + event 轴声明（<see cref="EventAxes"/>，单步瞬时、不进状态）+ 内建 const 轴（<c>"1"</c>）。
    /// 一份 schema 同时驱动：①变量→参数归约、②内层参数演进、③参数→标签解码。
    /// </summary>
    public sealed class ParameterSchema
    {
        /// <summary>内建常量轴 Id（唯一合法 const 轴）。</summary>
        public const string ConstAxisId = "1";

        /// <summary>内建常量轴定义（<see cref="AxisRole.Const"/>）。</summary>
        public static readonly ParameterSpec ConstAxis =
            new(ConstAxisId, ParameterKind.Numeric, Role: AxisRole.Const);

        private readonly IReadOnlyDictionary<string, ParameterSpec> _byName;
        private readonly IReadOnlyList<ParameterSpec> _ordered;
        private readonly IReadOnlyDictionary<string, ParameterSpec> _eventsByName;
        private readonly IReadOnlyList<ParameterSpec> _eventOrdered;

        /// <param name="specs">state 轴定义（<see cref="AxisRole.State"/>，即参数态的坐标系）。</param>
        /// <param name="eventAxes">event 轴声明（<see cref="AxisRole.Event"/>，单步瞬时、不进状态）；可为空。</param>
        public ParameterSchema(IEnumerable<ParameterSpec> specs, IEnumerable<ParameterSpec>? eventAxes = null)
        {
            if (specs is null) throw new ArgumentNullException(nameof(specs));
            _ordered = specs.ToList();
            var map = new Dictionary<string, ParameterSpec>(StringComparer.Ordinal);
            foreach (var spec in _ordered)
            {
                if (spec is null || string.IsNullOrWhiteSpace(spec.Name))
                    throw new ArgumentException("参数定义不允许 null/空白名。", nameof(specs));
                if (spec.Role != AxisRole.State)
                    throw new ArgumentException($"state 轴声明的 Role 必须为 State：{spec.Name}", nameof(specs));
                if (!map.TryAdd(spec.Name, spec))
                    throw new ArgumentException($"参数名重复：{spec.Name}", nameof(specs));
            }
            _byName = map;

            _eventOrdered = (eventAxes ?? Enumerable.Empty<ParameterSpec>()).ToList();
            var eventMap = new Dictionary<string, ParameterSpec>(StringComparer.Ordinal);
            foreach (var spec in _eventOrdered)
            {
                if (spec is null || string.IsNullOrWhiteSpace(spec.Name))
                    throw new ArgumentException("event 轴定义不允许 null/空白名。", nameof(eventAxes));
                if (spec.Role != AxisRole.Event)
                    throw new ArgumentException($"event 轴声明的 Role 必须为 Event：{spec.Name}", nameof(eventAxes));
                if (spec.IsForced)
                    throw new ArgumentException($"event 轴不允许 IsForced（瞬时轴不进状态、无强制写）：{spec.Name}", nameof(eventAxes));
                if (spec.IsLatched)
                    throw new ArgumentException($"event 轴不允许 IsLatched（吸收律只对状态主控位有意义）：{spec.Name}", nameof(eventAxes));
                if (!eventMap.TryAdd(spec.Name, spec))
                    throw new ArgumentException($"event 轴名重复：{spec.Name}", nameof(eventAxes));
            }
            _eventsByName = eventMap;
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

        /// <summary>event 轴声明（瞬时、不进状态演进；矩阵引用须以 (Event, axisId) 或唯一裸名声明）。</summary>
        public IReadOnlyList<ParameterSpec> EventAxes => _eventOrdered;

        /// <summary>按名查找 event 轴；未声明返回 null。</summary>
        public ParameterSpec? TryGetEvent(string name)
            => !string.IsNullOrEmpty(name) && _eventsByName.TryGetValue(name, out var s) ? s : null;

        /// <summary>按规范坐标 (role, axisId) 取轴定义；未声明返回 null（装载期闸A 的取值入口）。</summary>
        public ParameterSpec? TryGetAxis(AxisRole role, string axisId)
            => role switch
            {
                AxisRole.State => TryGet(axisId),
                AxisRole.Event => TryGetEvent(axisId),
                AxisRole.Const => ConstAxisId.Equals(axisId, StringComparison.Ordinal) ? ConstAxis : null,
                _ => null,
            };

        /// <summary>按规范坐标取轴定义；未声明抛 <see cref="KeyNotFoundException"/>。</summary>
        public ParameterSpec GetAxis(AxisRole role, string axisId)
            => TryGetAxis(role, axisId) ?? throw new KeyNotFoundException($"未声明的坐标轴：({role},{axisId})");

        /// <summary>该轴名在各 role 下的声明数（裸名解析歧义判定：0=未声明，1=可隐式解析，&gt;1=须显式带 role）。</summary>
        public int RoleCountOf(string axisId)
        {
            var n = 0;
            if (TryGetAxis(AxisRole.State, axisId) is not null) n++;
            if (TryGetAxis(AxisRole.Event, axisId) is not null) n++;
            if (TryGetAxis(AxisRole.Const, axisId) is not null) n++;
            return n;
        }

        /// <summary>构建默认参数态（各分量取 Default）。</summary>
        public ParameterState CreateDefault() => ParameterState.CreateDefault(this);
    }
}
