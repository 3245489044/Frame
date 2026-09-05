using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// Sqlite 只读表浏览通用工具 —— 表名白名单（防注入）+ 行数封顶 + 结果渲染为 Markdown 表格。
/// 下沉自 Agent.Launcher AgentUiLibraryDb 的 SQLite 只读浏览逻辑（SingleImplementation / TH-12）；
/// 只依赖 <see cref="SqliteSession"/>（连接串由调用方构造），保持只读 SELECT，绝不写入。
/// </summary>
public static class SqliteTableReader
{
    /// <summary>行数上限（缺省 200，最高 1000；防无界读库）。</summary>
    public static readonly int DefaultLimit = 200;
    private static readonly int MaxLimit = 1000;

    /// <summary>单元格文本最大长度（超出截断，避免撑爆 Markdown 表格）。</summary>
    public static readonly int CellMaxLength = 160;

    /// <summary>表名白名单判定（防注入）：仅允许 sqlite_master 中真实存在的业务表（排除 sqlite_* 系统表）。</summary>
    public static IReadOnlyList<string> ListUserTables(SqliteSession session)
        => session.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .Select(r => r.TryGetValue("name", out var v) ? v?.ToString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

    /// <summary>表是否存在于用户表白名单。</summary>
    public static bool IsUserTable(SqliteSession session, string? table)
        => !string.IsNullOrWhiteSpace(table)
            && ListUserTables(session).Contains(table!, StringComparer.OrdinalIgnoreCase);

    /// <summary>只读查询单表（表名白名单校验 + LIMIT 封顶）；表不存在/非法抛 <see cref="InvalidOperationException"/>。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> ReadRows(
        SqliteSession session, string table, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(table))
            throw new InvalidOperationException("（空表名）");
        if (!IsUserTable(session, table))
            throw new InvalidOperationException("（表不存在或非法）");
        var cap = Math.Max(1, Math.Min(limit ?? DefaultLimit, MaxLimit));
        return session.ExecuteQuery($"SELECT * FROM [{table}] LIMIT {cap}");
    }

    /// <summary>渲染行集为 Markdown 表格（空集 → （空表））；表不存在/非法返回错误信息字符串。</summary>
    public static string ReadRowsMarkdown(SqliteSession session, string table, int? limit = null)
    {
        try
        {
            return RenderMarkdown(ReadRows(session, table, limit));
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            return $"（读取失败）{ex.Message}";
        }
    }

    /// <summary>把行集渲染为 Markdown 表格（列 = 首行列键；单元格截断 + 管道/换行转义）。</summary>
    public static string RenderMarkdown(IReadOnlyList<IReadOnlyDictionary<string, object>> rows)
    {
        if (rows is null || rows.Count == 0) return "（空表）";
        var cols = rows[0].Keys.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", cols.Select(Escape)) + " |");
        sb.AppendLine("|" + string.Join("|", cols.Select(_ => "---")) + "|");
        foreach (var row in rows)
            sb.AppendLine("| " + string.Join(" | ", cols.Select(c => Escape(Truncate(row.TryGetValue(c, out var v) ? v?.ToString() : null)))) + " |");
        return sb.ToString();
    }

    /// <summary>单元格文本截断到 <see cref="CellMaxLength"/>（null → 空）。</summary>
    public static string Truncate(string? s, int max = 160)
        => string.IsNullOrEmpty(s) ? string.Empty : (s!.Length <= max ? s : s[..max] + "…");

    /// <summary>Markdown 单元格转义：管道 → 反斜杠转义，换行/回车 → 空格。</summary>
    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
