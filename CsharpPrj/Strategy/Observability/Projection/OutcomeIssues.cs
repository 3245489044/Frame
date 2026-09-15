namespace Strategy.Observability.Projection;

/// <summary>
/// 结果面原语（E17）：把「期望异常 → 结果清单」的捕获样板收敛为单一入口——
/// 不抛 = 空清单；期望异常 = 按其投影产出单元素清单；**非期望异常原样上抛**（不吞、不包、不降级）。
/// <para>
/// 动机：领域侧散布「<c>try { 校验(); return 空清单; } catch (期望异常 ex) { return 单条清单; }</c>」
/// 段，形态相似而捕获类型 / 投影口径各异，静默吞掉非期望异常的风险无法机检。
/// </para>
/// <para>
/// 投影函数参数化：调用方决定产出的是异常消息还是异常类型名（既有两类用法都须保真）。
/// </para>
/// </summary>
public static class OutcomeIssues
{
    /// <summary>
    /// 执行 <paramref name="body"/>：正常完成 → 空清单；
    /// 抛出 <paramref name="expected"/> 之一 → <c>[project(ex)]</c>；其余异常原样上抛。
    /// </summary>
    /// <param name="body">被执行体（其内部构造亦可落在体内，语义与原手写 try 段一致）。</param>
    /// <param name="project">期望异常 → 清单条目的投影（如 <c>ex =&gt; ex.Message</c> / <c>ex =&gt; ex.GetType().Name</c>）。</param>
    /// <param name="expected">期望异常类型集（空 = 无期望，任何异常均上抛）。</param>
    public static IReadOnlyList<string> Capture(Action body, Func<Exception, string> project, params Type[] expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(project);
        Validate(expected);

        try
        {
            body();
            return Array.Empty<string>();
        }
        catch (Exception ex) when (IsExpected(ex, expected))
        {
            return new[] { project(ex) };
        }
    }

    /// <summary>
    /// 异步版（语义与 <see cref="Capture"/> 一致）：体为 <see cref="Task"/> 时须 <c>await</c> 完成，
    /// 期望异常含异步抛出；非期望异常原样上抛。
    /// </summary>
    public static async Task<IReadOnlyList<string>> CaptureAsync(
        Func<Task> body, Func<Exception, string> project, params Type[] expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(project);
        Validate(expected);

        try
        {
            await body().ConfigureAwait(false);
            return Array.Empty<string>();
        }
        catch (Exception ex) when (IsExpected(ex, expected))
        {
            return new[] { project(ex) };
        }
    }

    /// <summary>期望类型判定（空集 = 无期望，恒 false ⇒ 异常一律上抛）。</summary>
    private static bool IsExpected(Exception ex, Type[] expected)
    {
        foreach (var type in expected)
            if (type.IsInstanceOfType(ex))
                return true;

        return false;
    }

    private static void Validate(Type[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        foreach (var type in expected)
            if (type is null)
                throw new ArgumentException("[outcome:issues] 期望异常类型集不得含 null 项", nameof(expected));
    }
}
