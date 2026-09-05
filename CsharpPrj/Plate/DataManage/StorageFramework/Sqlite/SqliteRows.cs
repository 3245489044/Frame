using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>Sqlite 行集读取与列访问共享助手。</summary>
public static class SqliteRows
{
    public static IReadOnlyDictionary<string, object> ReadRow(IDataReader reader)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var v = reader.GetValue(i);
            row[reader.GetName(i)] = v is DBNull ? null! : v;
        }
        return row;
    }

    public static string Str(IReadOnlyDictionary<string, object> row, string col)
        => row.TryGetValue(col, out var v) && v is not null ? v.ToString()! : string.Empty;

    public static string? Nullable(IReadOnlyDictionary<string, object> row, string col)
        => row.TryGetValue(col, out var v) && v is not null ? v.ToString() : null;

    public static long Long(IReadOnlyDictionary<string, object> row, string col, long fallback = 0)
        => row.TryGetValue(col, out var v) && v is not null && long.TryParse(v.ToString(), out var l) ? l : fallback;

    public static double Double(IReadOnlyDictionary<string, object> row, string col, double fallback)
        => row.TryGetValue(col, out var v) && v is not null && double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;
}
