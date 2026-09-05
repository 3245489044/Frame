namespace TestInfrastructure;

/// <summary>
/// 测试分层类别常量（供 <c>[TestCategory]</c> 标注），支撑 runner 按 <c>TestCategory</c> 过滤，
/// 将"核心逻辑秒级信号"与"完整回归"分离。约定：
/// <list type="bullet">
/// <item><see cref="Unit"/>：纯逻辑，秒级，默认回归范围；</item>
/// <item><see cref="Concurrency"/>：真实时序/并发/调度，需 [Timeout] 护栏；</item>
/// <item><see cref="Integration"/>：依赖外部服务/框架初始化；</item>
/// <item><see cref="Ui"/>：WinForms UI；</item>
/// <item><see cref="EndToEnd"/>：端到端。</item>
/// </list>
/// </summary>
public static class TestCategories
{
    public const string Unit = "Unit";
    public const string Concurrency = "Concurrency";
    public const string Integration = "Integration";
    public const string Ui = "Ui";
    public const string EndToEnd = "EndToEnd";
}
