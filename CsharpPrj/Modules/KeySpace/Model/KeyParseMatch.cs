namespace Modules.KeySpace.Model;

/// <summary>
/// 解析命中证据（结构事实，供诊断与门禁复算）：命中模板 + 是否只读兼容形态 + 特异性得分。
/// <para>只作为解析输出，不进入 <see cref="KeyInstance"/>（键一等值的相等性/哈希不得随模板变化）。</para>
/// </summary>
public sealed record KeyParseMatch(string Template, bool Compatible, int Score)
{
    /// <summary>空命中（未解析）。</summary>
    public static KeyParseMatch Empty { get; } = new(string.Empty, false, int.MinValue);
}
