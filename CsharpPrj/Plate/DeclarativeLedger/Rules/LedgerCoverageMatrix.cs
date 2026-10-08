using System.Text;

namespace BasicMethod.Declarative.Ledger;

/// <summary>矩阵单元格状态（三态）：已落地 / 确定缺失 / 无法判定（探针或证据未登记）。</summary>
public enum LedgerCellStatus
{
    /// <summary>已落地（探针 / 登记均通过）。</summary>
    Present,

    /// <summary>确定缺失（探针可判且判定为否）。</summary>
    Missing,

    /// <summary>无法判定（无探针 / 无证据；须显式补口或记豁免，**不得静默**）。</summary>
    Unregistered,
}

/// <summary>矩阵单元格：轴名（消费方注入，不透明）+ 状态 + 依据（可为空）。</summary>
public sealed record LedgerMatrixCell(string Axis, LedgerCellStatus Status, string? Detail = null);

/// <summary>矩阵行：族 + 词 + 各轴单元格（顺序 = 列注册序）。</summary>
public sealed record LedgerMatrixRow(string Family, string Word, IReadOnlyList<LedgerMatrixCell> Cells)
{
    /// <summary>是否存在任一「未落地」项（缺失 / 无法判定）。</summary>
    public bool HasGap => Cells.Any(cell => cell.Status != LedgerCellStatus.Present);

    /// <summary>确定缺失的轴。</summary>
    public IReadOnlyList<string> MissingAxes => Cells
        .Where(cell => cell.Status == LedgerCellStatus.Missing)
        .Select(cell => cell.Axis)
        .ToArray();

    /// <summary>无法判定的轴。</summary>
    public IReadOnlyList<string> UnregisteredAxes => Cells
        .Where(cell => cell.Status == LedgerCellStatus.Unregistered)
        .Select(cell => cell.Axis)
        .ToArray();
}

/// <summary>
/// 列轴数据源：对 <c>(族, 词)</c> 产出该轴单元格（消费方注册；框架只做组装、差集与棘轮）。
/// 零领域词：轴名 / 词 / 探针全部由消费方注入。
/// </summary>
public interface ILedgerMatrixColumn
{
    /// <summary>轴名（不透明；用于差集计数与渲染）。</summary>
    string Axis { get; }

    /// <summary>求值（只读；不抛）。</summary>
    LedgerMatrixCell Evaluate(string family, string word);
}

/// <summary>完备性矩阵报告（矩阵 + 差集 + 计数）。纯数据。</summary>
public sealed record LedgerMatrixReport(
    IReadOnlyList<LedgerMatrixRow> Rows, IReadOnlyList<string> Axes, string? Note = null)
{
    /// <summary>家族名（登记序，去重）。</summary>
    public IReadOnlyList<string> Families
        => Rows.Select(row => row.Family).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>词总数。</summary>
    public int WordCount => Rows.Count;

    /// <summary>差集（任一轴未落地的词）= 待办清单。</summary>
    public IReadOnlyList<LedgerMatrixRow> Gaps => Rows.Where(row => row.HasGap).ToArray();

    /// <summary>确定缺失项计数（按轴）。</summary>
    public IReadOnlyDictionary<string, int> MissingCounts => Count(LedgerCellStatus.Missing);

    /// <summary>无法判定项计数（按轴）；非零即「探针 / 证据未登记」待办。</summary>
    public IReadOnlyDictionary<string, int> UnregisteredCounts => Count(LedgerCellStatus.Unregistered);

    /// <summary>文本矩阵（基线落盘与人工复核用；列序 = 轴注册序）。</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("家族\t词\t" + string.Join('\t', Axes));
        foreach (var row in Rows)
            builder.AppendLine(
                row.Family + "\t" + row.Word + "\t"
                + string.Join('\t', row.Cells.Select(cell => cell.Status switch
                {
                    LedgerCellStatus.Present => "OK",
                    LedgerCellStatus.Missing => "MISS",
                    _ => "UNREG",
                })));

        return builder.ToString();
    }

    /// <summary>差集清单（人读形态：家族 / 词 / 轴 / 状态 / 依据）。</summary>
    public string RenderGaps()
    {
        var builder = new StringBuilder();
        foreach (var row in Gaps)
            foreach (var cell in row.Cells.Where(cell => cell.Status != LedgerCellStatus.Present))
                builder.AppendLine($"{row.Family}/{row.Word}\t{cell.Axis}\t{cell.Status}\t{cell.Detail}");

        return builder.ToString();
    }

    private IReadOnlyDictionary<string, int> Count(LedgerCellStatus status)
        => Rows.SelectMany(row => row.Cells)
            .Where(cell => cell.Status == status)
            .GroupBy(cell => cell.Axis, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}

/// <summary>
/// 阈值棘轮：某轴的「确定缺失 / 无法判定」计数**不得高于**上限（只降不升）。超限即产出违规。
/// </summary>
public sealed record LedgerCoverageThreshold(
    IReadOnlyDictionary<string, int> MaxMissing,
    IReadOnlyDictionary<string, int> MaxUnregistered)
{
    /// <summary>检查报告；返回违规（空 = 未超阈值）。</summary>
    public IReadOnlyList<string> Exceeded(LedgerMatrixReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var violations = new List<string>();
        foreach (var (axis, max) in MaxMissing)
            if (report.MissingCounts.TryGetValue(axis, out var actual) && actual > max)
                violations.Add($"{axis}: 确定缺失 {actual} > 棘轮上限 {max}（只降不升）");

        foreach (var (axis, max) in MaxUnregistered)
            if (report.UnregisteredCounts.TryGetValue(axis, out var actual) && actual > max)
                violations.Add($"{axis}: 无法判定 {actual} > 棘轮上限 {max}（只降不升）");

        return violations;
    }
}

/// <summary>矩阵组装（框架半）：「行 × 列」→ 报告。列轴重复登记即抛（防轴名漂移静默）。</summary>
public static class LedgerCoverageMatrix
{
    /// <summary>
    /// 组装矩阵：<paramref name="rows"/> = <c>(族, 词集)</c>（登记序即行序），
    /// <paramref name="columns"/> = 列轴数据源（登记序即列序）。
    /// </summary>
    public static LedgerMatrixReport Build(
        IEnumerable<(string Family, IReadOnlyList<string> Words)> rows,
        IEnumerable<ILedgerMatrixColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        var axes = new List<string>();
        var columnList = new List<ILedgerMatrixColumn>();
        var seenAxis = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentException.ThrowIfNullOrWhiteSpace(column.Axis);
            if (!seenAxis.Add(column.Axis))
                throw new DeclarationViolationException($"列轴重复登记：'{column.Axis}'", [column.Axis]);

            axes.Add(column.Axis);
            columnList.Add(column);
        }

        var matrix = new List<LedgerMatrixRow>();
        foreach (var (family, words) in rows)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(family);
            ArgumentNullException.ThrowIfNull(words);

            foreach (var word in words)
            {
                var cells = new LedgerMatrixCell[columnList.Count];
                for (var index = 0; index < columnList.Count; index++)
                    cells[index] = columnList[index].Evaluate(family, word);

                matrix.Add(new LedgerMatrixRow(family, word, cells));
            }
        }

        return new LedgerMatrixReport(matrix, axes);
    }
}
