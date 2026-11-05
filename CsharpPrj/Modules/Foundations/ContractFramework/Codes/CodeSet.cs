namespace ContractFramework.Codes;

/// <summary>
/// 受控码集（唯一实现）：码表即声明面，<see cref="All"/>/<see cref="IsKnown"/>/<see cref="Require"/> 由表派生。
/// <para>纪律：码表外的值一律不可判定；<see cref="Require"/> fail-fast（禁静默放行）。</para>
/// </summary>
public sealed class CodeSet
{
    private readonly HashSet<string> _codes;

    /// <param name="domain">码域（诊断/门禁定位用，非语义判断）。</param>
    /// <param name="codes">受控码（顺序即声明序；重复项只保留首现）。</param>
    public CodeSet(string domain, params string[] codes)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("码域不得为空。", nameof(domain));

        Domain = domain;
        var ordered = new List<string>(codes?.Length ?? 0);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("受控码不得为空。", nameof(codes));
            if (seen.Add(code)) ordered.Add(code);
        }

        _codes = seen;
        All = ordered.ToArray();
    }

    /// <summary>码域（结构定位；不参与语义判断）。</summary>
    public string Domain { get; }

    /// <summary>全部受控码（声明序；去重）。</summary>
    public IReadOnlyList<string> All { get; }

    /// <summary>码是否受控（空/未登记 ⇒ false）。</summary>
    /// <param name="code">待判定码。</param>
    public bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && _codes.Contains(code!);

    /// <summary>取受控码；空/未登记 ⇒ 抛（fail-fast，禁静默放行）。</summary>
    /// <param name="code">待校验码。</param>
    public string Require(string? code)
        => IsKnown(code)
            ? code!
            : throw new ArgumentException($"未登记的受控码：'{code}'（domain={Domain}）", nameof(code));
}
