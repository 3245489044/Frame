using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>闸A：<c>[Table]</c> 数据层形态（列类型/标志受控词、列名唯一）。</summary>
internal sealed class TableGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.table";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var table in model.Tables)
        {
            foreach (var line in table.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TableShapeInvalid,
                    $"[Table] {table.TableId}: 无法解析的列行 '{line}'", table.SourceFile, table.LineNumber));

            if (table.Container.Length == 0 || table.KeyColumn.Length == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TableShapeInvalid,
                    $"[Table] {table.TableId}: 缺 container= 或 key=", table.SourceFile, table.LineNumber));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns)
            {
                if (!seen.Add(column.Name))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TableColumnDuplicate,
                        $"[Table] {table.TableId}: 列 '{column.Name}' 重复声明", table.SourceFile, table.LineNumber));

                if (!UsharpLanguage.ColumnTypes.Contains(column.Type, StringComparer.OrdinalIgnoreCase))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TableColumnInvalid,
                        $"[Table] {table.TableId}.{column.Name}: 类型 '{column.Type}' 不在受控词（{string.Join("|", UsharpLanguage.ColumnTypes)}）",
                        table.SourceFile, table.LineNumber));

                foreach (var flag in column.Flags)
                {
                    if (!UsharpLanguage.ColumnFlags.Contains(flag, StringComparer.OrdinalIgnoreCase))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.TableColumnInvalid,
                            $"[Table] {table.TableId}.{column.Name}: 标志 '{flag}' 不在受控词（{string.Join("|", UsharpLanguage.ColumnFlags)}）",
                            table.SourceFile, table.LineNumber));
                }
            }
        }
    }
}

/// <summary>闸A：<c>[Record]</c> 形态与表引用（US7204/US7205/US7206）。</summary>
internal sealed class RecordGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.record";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var tableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in model.Tables)
        {
            tableIds.Add(table.TableId);
            if (!string.IsNullOrWhiteSpace(table.Container)) containers[table.Container] = table.TableId;
        }

        foreach (var record in model.Records)
        {
            foreach (var line in record.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RecordShapeInvalid,
                    $"[Record] {record.RecordId}: 无法解析的字段行 '{line}'", record.SourceFile, record.LineNumber));

            if (record.Table is not { Length: > 0 } tableId || tableIds.Contains(tableId)) continue;

            // E-15：先告警（兼容窗口），下一批收紧为 US7205。
            if (containers.TryGetValue(tableId, out var actualTableId))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RecordTableUsesContainer,
                    $"[Record] {record.RecordId}: table='{tableId}' 是表容器名，应写 table='{actualTableId}'"
                    + "（表 id 与容器 id 不得混同；兼容期告警）",
                    record.SourceFile, record.LineNumber));
                continue;
            }

            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RecordTableUndeclared,
                $"[Record] {record.RecordId}: 引用的表 '{tableId}' 未在本编译声明面中定义",
                record.SourceFile, record.LineNumber));
        }
    }
}

/// <summary>闸A：<c>[Rule]</c> 受控字段（<c>enabled=</c> 非法布尔不得静默变 null）。</summary>
internal sealed class RuleGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.rule";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var rule in model.Rules)
            if (rule.EnabledRaw is { } raw && !bool.TryParse(raw, out _))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RuleEnabledInvalid,
                    $"[Rule] {rule.Name}: enabled='{raw}' 非布尔值（合法：true|false）",
                    rule.SourceFile, rule.LineNumber));
    }
}
