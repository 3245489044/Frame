using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Strategy.Catalog.Core;
using Strategy.Catalog.Diagnostics;
using Strategy.Catalog.Model;

namespace Strategy.Catalog.Json;

/// <summary>
/// 一次目录查询请求（**统一入参形态**）—— 五件事共用同一结构，缺省字段按 op 取用。
/// <para>对齐既有工具面（<c>action</c>/<c>op</c> + 具名字段）与 MCP 工具入参习惯：一个对象走完全部分级查询。</para>
/// </summary>
/// <param name="Op">操作：<c>sources</c> / <c>index</c> / <c>list</c> / <c>describe</c> / <c>search</c> / <c>searchAll</c> / <c>stats</c>。</param>
/// <param name="SourceId">目录源 Id（<c>sources</c>/<c>searchAll</c> 可空）。</param>
/// <param name="AxisId">轴 Id（<c>list</c> 必填）。</param>
/// <param name="Value">轴取值（<c>list</c> 必填）。</param>
/// <param name="Key">条目键（<c>describe</c> 必填）。</param>
/// <param name="Query">检索词（<c>search</c>/<c>searchAll</c>；空 = 列全部但受限）。</param>
/// <param name="Limit">检索上限（null = <see cref="CatalogGrouper.DefaultSearchLimit"/>）。</param>
public sealed record CatalogQuery(
    string Op,
    string? SourceId = null,
    string? AxisId = null,
    string? Value = null,
    string? Key = null,
    string? Query = null,
    int? Limit = null);

/// <summary>
/// 目录查询 JSON 门面（**唯一文本出口**）—— 把五件事的结果序列化为稳定 JSON 形态，
/// 供工具面 / MCP / 脚本 / LLM 直接消费。
/// <para>
/// 错误不抛给消费方：统一收敛为 <c>{ status:"error", ... }</c>（**可见化**：带上已注册源清单/可用轴清单），
/// 使"查不到"永远有归因，不会变成静默空集。
/// </para>
/// </summary>
public static partial class CatalogJson
{
    /// <summary>JSON 序列化选项（不转义非 ASCII，缺省忽略 null）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string OpSources = "sources";
    public const string OpIndex = "index";
    public const string OpList = "list";
    public const string OpDescribe = "describe";
    public const string OpSearch = "search";
    public const string OpSearchAll = "searchAll";
    public const string OpStats = "stats";

    /// <summary>按 op 分发（大小写不敏感；未识别 op → 错误信封 + 可用 op 清单）。</summary>
    public static string Dispatch(CatalogQueryService service, CatalogQuery? query)
    {
        ArgumentNullException.ThrowIfNull(service);
        var op = query?.Op?.Trim();
        try
        {
            // 大小写不敏感匹配：常量是驼峰（"searchAll"），入参可能是任意大小写，禁止先归一后再做序敏感比较。
            if (string.IsNullOrEmpty(op) || IsOp(op, OpSources)) return Sources(service);
            if (IsOp(op, OpIndex)) return Index(service, query!.SourceId);
            if (IsOp(op, OpList)) return List(service, query!.SourceId, query.AxisId, query.Value);
            if (IsOp(op, OpDescribe)) return Describe(service, query!.SourceId, query.Key);
            if (IsOp(op, OpSearch)) return Search(service, query!.SourceId, query.Query, query.Limit);
            if (IsOp(op, OpSearchAll)) return SearchAll(service, query!.Query, query.Limit);
            if (IsOp(op, OpStats)) return Stats(service, query!.SourceId);

            return Error(op!, $"未知 op '{op}'；可用: {OpSources}/{OpIndex}/{OpList}/{OpDescribe}/{OpSearch}/{OpSearchAll}/{OpStats}", service);
        }
        catch (OperationCanceledException)
        {
            throw; // 取消不是查询失败：不伪装成 status:error
        }
        catch (Exception ex)
        {
            return Error(op ?? "(null)", MessageOf(ex), service);
        }
    }

    private static bool IsOp(string op, string expected)
        => string.Equals(op, expected, StringComparison.OrdinalIgnoreCase);

    private static string MessageOf(Exception ex)
        => ex is CatalogException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>有哪些目录可查（最外层索引）。</summary>
    public static string Sources(CatalogQueryService service)
    {
        var list = service.Sources();
        return Serialize(new
        {
            status = "ok",
            op = OpSources,
            count = list.Count,
            sources = list.Select(s => new
            {
                sourceId = s.SourceId,
                display = s.Display,
                axisCount = s.AxisCount,
                total = s.Total,
            }),
        });
    }

    /// <summary>一级索引。</summary>
    public static string Index(CatalogQueryService service, string? sourceId)
    {
        var index = service.Index(sourceId);
        return Serialize(new
        {
            status = "ok",
            op = OpIndex,
            tier = "index",
            sourceId = index.SourceId,
            display = index.Display,
            total = index.Total,
            axes = index.Axes.Select(a => new { id = a.Id, display = a.EffectiveDisplay }),
            pinned = index.Pinned.Select(ProjectItem),
            groups = index.Groups.Select(ProjectGroup),
        });
    }

    /// <summary>二级目录（按轴取值）。</summary>
    public static string List(CatalogQueryService service, string? sourceId, string? axisId, string? value)
    {
        var list = service.List(sourceId, axisId, value);
        return Serialize(new
        {
            status = "ok",
            op = OpList,
            tier = "group",
            sourceId = list.SourceId,
            axis = list.AxisId,
            axisDisplay = list.AxisDisplay,
            value = list.Value,
            display = list.Display,
            count = list.Count,
            items = list.Items.Select(ProjectItem),
        });
    }

    /// <summary>错误信封（含已注册源清单，供自纠）。</summary>
    public static string Error(string op, string message, CatalogQueryService? service = null)
    {
        var registered = service?.Registry.Sources
            .Where(s => s is not null)
            .Select(s => s.SourceId)
            .ToList();
        return Serialize(new
        {
            status = "error",
            op,
            error = message,
            registeredSources = registered is { Count: > 0 } ? registered : null,
        });
    }

    private static string Serialize(object payload)
        => JsonSerializer.Serialize(payload, Options);
}
