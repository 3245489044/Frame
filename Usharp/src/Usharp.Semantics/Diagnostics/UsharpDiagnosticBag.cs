namespace Usharp.Semantics;

/// <summary>
/// 有序诊断袋：按 <c>(Code, File, Line, Message)</c> 去重，避免同一缺陷在闸A/发射/适配多处重复计入。
/// </summary>
public sealed class UsharpDiagnosticBag
{
    private readonly List<UsharpDiagnostic> _items = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    /// <summary>诊断集（加入序）。</summary>
    public IReadOnlyList<UsharpDiagnostic> Items => _items;

    /// <summary>加入一条诊断（重复 ⇒ false，不改变顺序）。</summary>
    /// <param name="diagnostic">诊断。</param>
    public bool Add(UsharpDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var key = Key(diagnostic);
        if (!_keys.Add(key)) return false;
        _items.Add(diagnostic);
        return true;
    }

    /// <summary>批量加入。</summary>
    public void AddRange(IEnumerable<UsharpDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        foreach (var diagnostic in diagnostics) Add(diagnostic);
    }

    /// <summary>是否含 error（判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    public bool HasErrors => UsharpDiagnostics.HasError(_items);

    private static string Key(UsharpDiagnostic d)
        => $"{d.Code}\u0000{d.SourceFile}\u0000{d.LineNumber}\u0000{d.Message}";
}
