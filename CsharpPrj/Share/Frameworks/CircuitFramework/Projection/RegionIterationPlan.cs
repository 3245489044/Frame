namespace Share.CircuitFramework.Projection;

/// <summary>
/// 区域迭代受控词：区域（环）在父图 DAG **内部**的重复方式。
/// <para>环不外化为回边（Wire 成环仍被 CFG-002 禁）—— 迭代被封在区域内，父图保持 DAG 可拓扑排序。</para>
/// </summary>
public enum RegionIterationKind
{
    /// <summary>单趟：子计划执行一次（不成环；区域化复用的最小形态）。</summary>
    Once,

    /// <summary>有界计次重复（折叠语义，非回边）。</summary>
    FixedRepeats,

    /// <summary>上判定条件迭代：判定端口为 true 时继续迭代。</summary>
    WhileTrue,

    /// <summary>后判定条件迭代：判定端口为 true 时停止迭代。</summary>
    UntilTrue,
}

/// <summary>
/// 区域迭代计划（声明面）：迭代方式 + 计次 + 判定端口键 + **迭代上限**。
/// <para>
/// R1（有界量化）：一切迭代必须声明上限（<see cref="MaxIterations"/> 必填），无界量化禁入 ——
/// 框架不承担开放探索的停机判定（停机责任外置，框架只提供"图能表达并执行带反馈的执行"）。
/// </para>
/// <para>
/// 判定载体 = **端口键**（<see cref="JudgePort"/>），不是表达式文本：判定值只从端口数据取
/// （判据载体须为结构事实，声明文案仅作对账）。
/// </para>
/// </summary>
/// <param name="Kind">迭代方式。</param>
/// <param name="MaxIterations">迭代上限（R1 必填；null 或非法值由 CFG-022 判红）。</param>
/// <param name="Repeats">计次（仅 <see cref="RegionIterationKind.FixedRepeats"/> 语义；0 = 零次迭代，合法）。</param>
/// <param name="JudgePort">判定端口键（仅条件迭代语义；父区域节点的入端口名）。</param>
public sealed record RegionIterationPlan(
    RegionIterationKind Kind,
    int? MaxIterations,
    int Repeats = 0,
    string? JudgePort = null)
{
    /// <summary>单趟执行一次（上限恒为 1）。</summary>
    public static RegionIterationPlan Once() => new(RegionIterationKind.Once, 1);

    /// <summary>有界计次重复（上限即计次；<paramref name="repeats"/> = 0 合法 = 零次迭代，作负对照）。</summary>
    public static RegionIterationPlan Fixed(int repeats) => new(RegionIterationKind.FixedRepeats, repeats, repeats);

    /// <summary>上判定条件迭代（判定端口为 true 时继续；<paramref name="maxIterations"/> 须 &gt; 0）。</summary>
    public static RegionIterationPlan WhileTrue(string judgePort, int maxIterations)
        => new(RegionIterationKind.WhileTrue, maxIterations, 0, judgePort);

    /// <summary>后判定条件迭代（判定端口为 true 时停止；<paramref name="maxIterations"/> 须 &gt; 0）。</summary>
    public static RegionIterationPlan UntilTrue(string judgePort, int maxIterations)
        => new(RegionIterationKind.UntilTrue, maxIterations, 0, judgePort);
}
