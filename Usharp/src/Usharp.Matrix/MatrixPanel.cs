using BasicMethod.Declarative.Spec;
using Strategy.DecisionRules;

namespace Usharp.Matrix;

/// <summary>
/// 值域矩阵**求值面**（通用）：持一份 <c>[Matrix] over=&lt;kind=values 空间&gt;</c> 的 schema + 矩阵，
/// 以既有 <see cref="ParameterMatrix.ApplyTo"/> 承担「初始态 / 一步演进 / 取值」这类**纯逻辑**演算
/// （该不该再试、推进到哪）——零自建解释器（禁第二份求值实现，见 <c>Strategy.SingleImplementation</c>）。
/// <para>
/// 边界：真正的 sleep / kill / 落库是**效果**，落消费方的 <c>[Node]</c> 效果端口（宿主实现），不进本类。
/// </para>
/// <para>
/// 不激活语义：矩阵不可求值 ⇒ 工厂返回 <c>null</c>，调用方**不得**回退到自算（那会造第二真源），
/// 只能不激活并可见化。
/// </para>
/// </summary>
public sealed class MatrixPanel
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyEvents =
        new Dictionary<string, object?>(0, StringComparer.Ordinal);

    private readonly Action<string, object?>? _log;
    private readonly string _matrixId;
    private readonly ParameterSchema _schema;
    private readonly ParameterMatrix _matrix;

    private MatrixPanel(string matrixId, ParameterSchema schema, ParameterMatrix matrix, Action<string, object?>? log)
    {
        _matrixId = matrixId;
        _schema = schema;
        _matrix = matrix;
        _log = log;
    }

    /// <summary>矩阵号（声明面 <c>[Matrix]</c> 名）。</summary>
    public string MatrixId => _matrixId;

    /// <summary>矩阵覆盖的参数轴（= 对应 <c>[Space] kind=values</c> 坐标序）。</summary>
    public IReadOnlyList<string> Axes => _schema.Specs.Select(s => s.Name).ToList();

    /// <summary>
    /// 由已发射的 schema + 矩阵创建求值面（任一为 null ⇒ null，不激活）。
    /// 消费方自持「矩阵号 → schema/矩阵」的映射（本类不做声明面解析，保持零装载依赖）。
    /// </summary>
    /// <param name="matrixId">矩阵号。</param>
    /// <param name="schema">矩阵坐标系。</param>
    /// <param name="matrix">矩阵实例。</param>
    /// <param name="log">告警端口（<c>(事件键, 载荷)</c>）；未注入 ⇒ 静默，由消费方自行承担可见化。</param>
    public static MatrixPanel? Create(
        string matrixId,
        ParameterSchema? schema,
        ParameterMatrix? matrix,
        Action<string, object?>? log = null)
        => string.IsNullOrWhiteSpace(matrixId) || schema is null || matrix is null
            ? null
            : new MatrixPanel(matrixId, schema, matrix, log);

    /// <summary>由一次矩阵装配的绑定创建求值面（非值域读法 / 缺 schema / 缺矩阵 ⇒ null）。</summary>
    /// <param name="binding">矩阵装配产物。</param>
    /// <param name="log">告警端口。</param>
    public static MatrixPanel? TryFrom(UsharpMatrixBinding? binding, Action<string, object?>? log = null)
        => binding is null
            ? null
            : Create(binding.MatrixId, binding.Schema, binding.ParameterMatrix, log);

    /// <summary>按矩阵号从一次装配结果中创建求值面（未声明该矩阵号 ⇒ null）。</summary>
    /// <param name="emit">一次矩阵装配的批产物。</param>
    /// <param name="matrixId">矩阵号。</param>
    /// <param name="log">告警端口。</param>
    public static MatrixPanel? TryFrom(UsharpMatrixEmitResult? emit, string matrixId, Action<string, object?>? log = null)
        => emit is not null && emit.TryGet(matrixId, out var binding) ? TryFrom(binding, log) : null;

    /// <summary>
    /// 初始态：<paramref name="initial"/> 给出调用方权威的初值（不变量：初始态由调用方给定，
    /// 声明面只承担**演进**语义），其余轴取 schema 默认值。
    /// </summary>
    /// <param name="initial">初始轴值（null / 空 ⇒ 全默认）。</param>
    public IReadOnlyDictionary<string, double> Start(IReadOnlyDictionary<string, double>? initial = null)
        => Snapshot(Seed(initial));

    /// <summary>
    /// 一步演进：<c>x′ = Quantize(W·x + b)</c>（声明面右式），返回新态。
    /// 纯函数（无内部可变状态 ⇒ 可安全跨线程复用）；<paramref name="step"/> 仅作步序留痕。
    /// </summary>
    /// <param name="current">当前态（缺失轴按 schema 默认）。</param>
    /// <param name="step">步序（0 起）。</param>
    /// <param name="events">本步事件变量（声明面若引用 event 轴则须提供）。</param>
    public IReadOnlyDictionary<string, double> Next(
        IReadOnlyDictionary<string, double> current,
        int step,
        IReadOnlyDictionary<string, object?>? events = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        var state = Seed(current);
        var writes = _matrix.ApplyTo(new ParametricStepInput(state, step, events ?? EmptyEvents));
        var next = state.Apply(writes, out var rejected);

        if (rejected.Count > 0)
        {
            // 越权写（非 Engine 来源触碰强制分量）不应发生：可见化而非静默（DeterministicCoreDisjoint）。
            _log?.Invoke("MatrixPanel_rejected_write", new { matrix = _matrixId, axes = string.Join(",", rejected) });
        }

        return Snapshot(next);
    }

    /// <summary>取某轴值（缺失 ⇒ 0）。</summary>
    /// <param name="state">矩阵态。</param>
    /// <param name="axis">轴名。</param>
    public static double ValueOf(IReadOnlyDictionary<string, double> state, string axis)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TryGetValue(axis ?? string.Empty, out var value) ? value : 0;
    }

    /// <summary>该轴值是否为正（&gt; 0）——「是否仍有剩余预算」这类纯逻辑判读的通用形态。</summary>
    /// <param name="state">矩阵态。</param>
    /// <param name="axis">轴名。</param>
    public static bool IsPositive(IReadOnlyDictionary<string, double> state, string axis)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.TryGetValue(axis ?? string.Empty, out var value) && value > 0;
    }

    private ParameterState Seed(IReadOnlyDictionary<string, double>? values)
    {
        var state = ParameterState.CreateDefault(_schema);
        if (values is null || values.Count == 0) return state;

        var writes = new List<ParameterWrite>();
        foreach (var (name, value) in values)
        {
            if (_schema.TryGet(name) is null) continue;   // 非声明坐标：剔除（闸A 已保证声明面内部一致）
            writes.Add(ParameterWrite.Engine(name, value));
        }
        return state.Apply(writes);
    }

    private static IReadOnlyDictionary<string, double> Snapshot(ParameterState state) => state.Values;
}
