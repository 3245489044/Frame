using System;
using System.Collections.Generic;
using Modules.Parsing.Invocation;

namespace CommandFramework.Execution.Commands;

/// <summary>
/// 命令参数读取门面（零语义）：全部语义由调用面唯一读取器 <c>InvocationArgs</c> 定义，
/// 本类只做转发，禁止再出现任何解析/缺省/切分实现（结构判据：本文件无 TryParse/Split/TryGetValue）。
/// </summary>
/// <remarks>
/// 保留本类名仅为兼容既有命令系统调用点；新增代码直接使用 <c>InvocationArgs</c>。
/// </remarks>
/// <para>
/// 语义约定（与既有命令面保持一致）：
/// <list type="bullet">
/// <item>缺失、值为 null、空白字符串 → 视为「未提供」，返回缺省值。</item>
/// <item>bool 接受 true/1（忽略大小写），其余均 false。</item>
/// <item>int/long 使用 InvariantCulture；非法值 → 未提供（返回缺省），不抛异常。</item>
/// <item>string 直接 ToString()（保留原始数值形态）。</item>
/// </list>
/// </para>
public static class CommandParameters
{
    public static bool Contains(IReadOnlyDictionary<string, object?>? parameters, string key)
        => InvocationArgs.Read(parameters, key).Present;

    /// <summary>读取 bool；缺失/非法 → <paramref name="defaultValue"/>。</summary>
    public static bool GetBool(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        bool defaultValue = false)
        => InvocationArgs.Bool(parameters, key, defaultValue);

    /// <summary>
    /// 读取 int：返回 (是否存在且可解析, 值)。存在但非法 → (false, 0)（调用方可据此回退缺省）。
    /// 用于「显式 timeout=0 表示无限」这类需要区分「未提供」与「提供 0」的场景。
    /// </summary>
    public static (bool Present, int Value) TryGetInt(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        var read = InvocationArgs.Int(parameters, key);
        return read is { Present: true, Valid: true } ? (true, read.Value) : (false, 0);
    }

    /// <summary>读取 int；缺失/非法 → <paramref name="defaultValue"/>。</summary>
    public static int GetInt(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        int defaultValue)
        => TryGetInt(parameters, key) is { Present: true } result ? result.Value : defaultValue;

    /// <summary>读取 long；缺失/非法 → null。</summary>
    public static long? GetLong(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
        => InvocationArgs.Long(parameters, key);

    /// <summary>读取字符串；缺失/空白 → null。</summary>
    public static string? GetString(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
        => InvocationArgs.Text(parameters, key);

    /// <summary>读取字符串；缺失/空白 → <paramref name="defaultValue"/>。</summary>
    public static string GetString(
        IReadOnlyDictionary<string, object?>? parameters,
        string key,
        string defaultValue)
        => InvocationArgs.Text(parameters, key) ?? defaultValue;

    /// <summary>
    /// 读取词表（failWhen/failContains 等）：按逗号/竖线/空格切分，去重（忽略大小写）。
    /// 缺失 → 空数组（永不为 null，便于调用方直接 Any/Contains）。
    /// </summary>
    public static IReadOnlyList<string> GetTokens(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
        => InvocationArgs.Tokens(parameters, key);
}
