using System.Security.Cryptography;
using System.Text;

namespace BasicMethod.Declarative.Ledger;

/// <summary>枚举绑定：结构门里的枚举**键路径**（不透明，如 <c>$defs/impl/properties/kind/enum</c>）→ 词表（唯一源）。</summary>
public sealed record LedgerEnumBinding(string KeyPath, LedgerTerm Term);

/// <summary>枚举变更项（键路径 + 期望词序列 + 实得词序列；<c>Actual=null</c> = 位置缺失）。</summary>
public sealed record LedgerEnumChange(string KeyPath, IReadOnlyList<string> Expected, IReadOnlyList<string>? Actual);

/// <summary>
/// 枚举对账计划：把一组 <see cref="LedgerEnumBinding"/> 与结构门的**实得值**逐项比对，产出差异清单。
/// <para>
/// 只读（不写盘）：写回是离线工具的职责（先对拍后落盘）。差异口径 = 集合相等 **且** 词序一致
/// （词序漂移属可见变更，须显式再生，禁静默）。位置缺失与值漂移分别回报。
/// </para>
/// </summary>
public static class LedgerEnumPlan
{
    /// <summary>
    /// 变更集（**写回半 · 只读**）：逐绑定对账，返回**需变更项**（保持绑定序）。
    /// <paramref name="actualOf"/>(键路径) → 实得词序列（null = 位置缺失）。
    /// 差异口径 = 集合相等 **且** 词序一致（词序漂移属可见变更）。本件不写盘——写回是离线工具的职责。
    /// </summary>
    public static IReadOnlyList<LedgerEnumChange> Plan(
        IEnumerable<LedgerEnumBinding> bindings,
        Func<string, IReadOnlyList<string>?> actualOf)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(actualOf);

        var changes = new List<LedgerEnumChange>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);

            var actual = actualOf(binding.KeyPath);
            if (actual is not null && actual.SequenceEqual(binding.Term.Words, StringComparer.Ordinal)) continue;

            changes.Add(new LedgerEnumChange(binding.KeyPath, binding.Term.Words, actual));
        }

        return changes;
    }

    /// <summary>
    /// 差异文案（由 <see cref="Plan"/> 渲染；<paramref name="messageFactory"/> 注入，缺省给通用文案）。
    /// </summary>
    public static IReadOnlyList<string> Verify(
        IEnumerable<LedgerEnumBinding> bindings,
        Func<string, IReadOnlyList<string>?> actualOf,
        Func<string, string>? messageFactory = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        string Describe(string detail) => messageFactory?.Invoke(detail) ?? detail;

        var diffs = new List<string>();
        foreach (var change in Plan(bindings, actualOf))
        {
            diffs.Add(change.Actual is null
                ? Describe($"{change.KeyPath}：位置缺失（期望 [{string.Join(", ", change.Expected)}]）")
                : Describe($"{change.KeyPath}：词表漂移（期望 [{string.Join(", ", change.Expected)}]，"
                           + $"实得 [{string.Join(", ", change.Actual)}]）"));
        }

        return diffs;
    }

    /// <summary>全绿判定（无差异）。</summary>
    public static bool IsAligned(
        IEnumerable<LedgerEnumBinding> bindings,
        Func<string, IReadOnlyList<string>?> actualOf)
        => Verify(bindings, actualOf).Count == 0;

    /// <summary>
    /// 期望内容**指纹**（键路径 + 词序的稳定 SHA-256，大写十六进制）：供「生成物指纹断言」用
    /// ——消费方把产物归一到绑定集后重算指纹，不等即手改/漂移（当场可见，禁静默）。
    /// 指纹与**输入序无关**（按键路径 Ordinal 排序后计算）。
    /// </summary>
    public static string Fingerprint(IEnumerable<LedgerEnumBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var canonical = new StringBuilder();
        foreach (var binding in bindings.OrderBy(item => item.KeyPath, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(binding);
            canonical.Append(binding.KeyPath).Append('\t').Append(binding.Term.Render()).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
