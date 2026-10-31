namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 边断言强度（DP4：每条边必携带，缺省即阻断；禁隐式 declared）。
/// <list type="bullet">
/// <item><see cref="Declared"/> —— 编译期可静态检查（声明面唯一来源）。</item>
/// <item><see cref="Observed"/> —— 运行期实测 provenance（与写同事务盖章）。</item>
/// <item><see cref="May"/> —— 不可判定依赖的保守近似（禁沉默略过）。</item>
/// </list>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.2。
/// </summary>
public enum DependencyAssertion
{
    /// <summary>声明面给出，可机械重算。</summary>
    Declared = 0,

    /// <summary>运行期实测，随写路径同事务产生。</summary>
    Observed = 1,

    /// <summary>不可判定依赖的保守近似（LLM 检索 / 分支选择）。</summary>
    May = 2,
}

/// <summary>
/// 断言强度的线上词形（跨语言交换的唯一拼写源）—— 禁在投影/查询/门禁各处手写字面量
/// （权威: axioms:Type.UnknownTokens 同款单源纪律）。
/// </summary>
public static class DependencyAssertions
{
    /// <summary>声明面断言词形。</summary>
    public const string Declared = "declared";

    /// <summary>运行期观测断言词形。</summary>
    public const string Observed = "observed";

    /// <summary>保守近似断言词形。</summary>
    public const string May = "may";

    /// <summary>枚举 → 线上词形（未登记值 fail-fast，禁静默回落 declared）。</summary>
    public static string ToWire(DependencyAssertion assertion) => assertion switch
    {
        DependencyAssertion.Declared => Declared,
        DependencyAssertion.Observed => Observed,
        DependencyAssertion.May => May,
        _ => throw new ArgumentOutOfRangeException(nameof(assertion), assertion, "未登记的断言强度（禁静默回落）。"),
    };

    /// <summary>线上词形 → 枚举（未登记词形返回 false，由调用方决定显式缺口）。</summary>
    public static bool TryParse(string? wire, out DependencyAssertion assertion)
    {
        switch (wire)
        {
            case Declared:
                assertion = DependencyAssertion.Declared;
                return true;
            case Observed:
                assertion = DependencyAssertion.Observed;
                return true;
            case May:
                assertion = DependencyAssertion.May;
                return true;
            default:
                assertion = default;
                return false;
        }
    }
}
