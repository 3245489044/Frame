#nullable enable

using System;
using System.Collections.Generic;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 通用数据页状态（选择集 / 分页 / 过滤 / 排序 / 展开详情 / 最近结果 / 二次确认待决）。
/// <para>
/// 纯状态载体，零领域类型；页面域只追加「当前域」等自己的标签字段。
/// 纪律：写入只发生在命令期，渲染路径只读（渲染纯化）。
/// </para>
/// </summary>
public class DataPageState
{
    /// <summary>勾选集（行身份条目 <c>domain␟key</c>；域感知，同键跨域可辨）。</summary>
    public readonly HashSet<string> Selected = new(StringComparer.Ordinal);

    /// <summary>最近一次批量结果（逐项清单；渲染为顶部横幅，非静默）。</summary>
    public string? LastResult;

    /// <summary>待二次确认的命令（null=无）。</summary>
    public string? PendingConfirmCommand;

    /// <summary>待二次确认的键集快照（与当前集不一致则视为新一轮）。</summary>
    public string[] PendingConfirmKeys = Array.Empty<string>();

    /// <summary>当前页（0 基；渲染时按总页数夹取）。</summary>
    public int Page;

    /// <summary>当前展开详情的节点键（写身份第二段 = 真键；null=无）。</summary>
    public string? ExpandedKey;

    /// <summary>当前展开详情的写域（写身份第一段；保存/删除的路由判据）。</summary>
    public string? DetailWriteDomain;

    /// <summary>展开节点的只读字段（保存时原样回写，避免改坏身份）。</summary>
    public string? DetailNodeType;

    public string? DetailThreadId;

    public string? DetailKey;

    public string? DetailStatus;

    /// <summary>展开节点的节注（不可编辑原因 / 记录未找到）。</summary>
    public string? DetailNote;

    /// <summary>展开节点的整记录编辑器文本（读=写同一物）。</summary>
    public string? DetailRecordJson;

    /// <summary>展开节点的装载基线（供「恢复为改前」）。</summary>
    public string? DetailBaselineJson;

    /// <summary>过滤子串（空 = 不过滤）。</summary>
    public string? Filter;

    /// <summary>排序键（空 = 采集序）。</summary>
    public string? SortKey;

    /// <summary>排序方向（asc/desc；默认 asc）。</summary>
    public string SortOrder = "asc";
}

/// <summary>
/// 通用数据页查询/分页运算 —— 页码夹取、切片与页码文案的单源实现；过滤/排序/分组见 <see cref="DataPagePlan"/>。
/// 领域只提供取值选择器与匹配谓词；本类零领域类型。
/// </summary>
public static class DataPageQuery
{

    /// <summary>页码夹取（渲染期纯投影；越界不回写交互态）。</summary>
    public static (int Page, int PageCount) ClampPage(int page, int totalRows, int pageSize)
    {
        var size = Math.Max(1, pageSize);
        var count = totalRows <= 0 ? 1 : (totalRows + size - 1) / size;
        var clamped = Math.Clamp(page, 0, count - 1);
        return (clamped, count);
    }

    /// <summary>分页行切片（页码已夹取）。</summary>
    public static List<T> SlicePage<T>(IReadOnlyList<T> rows, int page, int pageSize)
    {
        var size = Math.Max(1, pageSize);
        var start = Math.Max(0, page) * size;
        if (start >= rows.Count) return new List<T>();
        var take = Math.Min(size, rows.Count - start);
        var result = new List<T>(take);
        for (var i = 0; i < take; i++) result.Add(rows[start + i]);
        return result;
    }

    /// <summary>页码文案（默认 <c>第 x/y 页</c>；数值格式单源 = <see cref="MarkupValue.Page"/>，语言文案由调用方参数注入）。</summary>
    public static string PageText(
        int page,
        int pageCount,
        string prefix = "第 ",
        string suffix = " 页")
        => prefix + MarkupValue.Page(page + 1, pageCount) + suffix;
}
