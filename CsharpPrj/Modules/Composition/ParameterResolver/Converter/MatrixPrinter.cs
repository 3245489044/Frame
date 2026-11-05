
using System;
using System.Collections.Generic;
using System.Linq;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Converter;

public static class MatrixPrinter
{
    public static string Print<T>(string title, IEnumerable<T> rows)
        where T : IMetadataProvider<T>
    {
        var columns = T.GetColumns();
        if (columns == null || columns.Count == 0)
            return $"{title} (0):";

        var header = string.Join(",", columns.Select(c => c.Header));

        var dataRows = rows.Select(row =>
            string.Join(",",
                columns.Select(c => FormatCell(c.ValueExtractor(row)))));

        var items = rows.Count();
        var allLines = new List<string>(items + 2);
        allLines.Add($"{title} ({items}):");
        allLines.Add(header);
        allLines.AddRange(dataRows);
        return string.Join(Environment.NewLine, allLines);
    }

    public static string Print<T>(
        string title,
        IEnumerable<T> rows,
        params (string Header, Func<T, object?> Selector)[] columns)
    {
        if (columns == null || columns.Length == 0)
            return $"{title} (0):";

        var header = string.Join(",", columns.Select(c => c.Header));

        var dataRows = rows.Select(row =>
            string.Join(",",
                columns.Select(c => FormatCell(c.Selector(row)))));

        var allLines = new List<string>(rows.Count() + 2);
        allLines.Add($"{title} ({rows.Count()}):");
        allLines.Add(header);
        allLines.AddRange(dataRows);
        return string.Join(Environment.NewLine, allLines);
    }

    private static string FormatCell(object? value)
    {
        if (value == null)
            return "-";
        if (value is string s && string.IsNullOrEmpty(s))
            return "-";
        return value.ToString() ?? "-";
    }
}
