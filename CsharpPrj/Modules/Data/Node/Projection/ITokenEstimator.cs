namespace Modules.Node.Projection;

/// <summary>
/// token 估算端口（<b>宿主注入</b>：框架不内置「N 字符/token」等业务常量）。
/// </summary>
public interface ITokenEstimator
{
    /// <summary>估算文本 token 数（非负）。</summary>
    int Estimate(string text);
}

/// <summary>声明驱动的字符- token 估算器（换算比例来自声明 <c>budget.charsPerToken</c>）。</summary>
public sealed class DeclaredTokenEstimator : ITokenEstimator
{
    /// <summary>按声明比例构造（比例须 &gt; 0）。</summary>
    public DeclaredTokenEstimator(int charsPerToken)
    {
        if (charsPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(charsPerToken), charsPerToken, "字符-token 比例须 > 0");
        }

        CharsPerToken = charsPerToken;
    }

    /// <summary>字符-token 换算比例。</summary>
    public int CharsPerToken { get; }

    /// <inheritdoc />
    public int Estimate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (text.Length + CharsPerToken - 1) / CharsPerToken;
    }
}

/// <summary>
/// 未装配估算器（N9：缺口可见化，禁静默采用缺省比例）。
/// 估算恒 0，调用方须按「无估算」语义处理（不截断，且报告端口缺失）。
/// </summary>
public sealed class NullTokenEstimator : ITokenEstimator
{
    /// <summary>单例。</summary>
    public static readonly NullTokenEstimator Instance = new();

    private NullTokenEstimator()
    {
    }

    /// <inheritdoc />
    public int Estimate(string text) => 0;
}
