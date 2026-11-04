namespace MathematicalAlgorithms.Formula;

/// <summary>拟合记录：方法、窗口、样本数、证据来源。</summary>
public sealed record FormulaFitRecord(
    string Method,
    FormulaFitWindow Window,
    int Samples,
    IReadOnlyList<FormulaSource> Sources)
{
    public static FormulaFitRecord Empty { get; } = new(
        "none",
        new FormulaFitWindow("", "", new SortedDictionary<string, string>(StringComparer.Ordinal)),
        0,
        Array.Empty<FormulaSource>());

    public FormulaFitRecord Normalize()
        => this with
        {
            Window = Window?.Normalize()
                ?? new FormulaFitWindow("", "", new SortedDictionary<string, string>(StringComparer.Ordinal)),
            Sources = (Sources ?? Array.Empty<FormulaSource>())
                .OrderBy(static s => s.File, StringComparer.Ordinal)
                .ThenBy(static s => s.Section, StringComparer.Ordinal)
                .ToArray(),
        };
}
