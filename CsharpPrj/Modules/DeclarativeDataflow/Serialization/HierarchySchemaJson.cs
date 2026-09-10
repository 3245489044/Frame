using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Serialization;

/// <summary>
/// 层次 schema 的声明式 JSON 装载 / 输出（"层次外置"的落地形态之一）：层数 N 与包含关系写在数据里，而非代码里。
/// <para>
/// 例子（异构 5 层 + 自递归任务树混合）：
/// <code>
/// {
///   "rootLevelId": "project",
///   "levels": [
///     { "levelId": "project", "entityType": "project", "processId": "plan",
///       "contains": [ { "childLevelId": "history", "containsEdgeType": "project_owns_history" } ] },
///     { "levelId": "history", "entityType": "history",
///       "contains": [ { "childLevelId": "task", "containsEdgeType": "history_owns_task" } ] },
///     { "levelId": "task", "entityType": "task", "processId": "task-run",
///       "contains": [
///         { "childLevelId": "task", "containsEdgeType": "task_owns_task" },
///         { "childLevelId": "node", "containsEdgeType": "cross_db_link" } ] },
///     { "levelId": "node", "entityType": "node", "processId": "node-round", "contains": [] }
///   ]
/// }
/// </code>
/// </para>
/// </summary>
public static class HierarchySchemaJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 解析 schema JSON。
    /// <paramref name="validate"/> 为真时额外执行**语义完整性**校验（可达 / 悬空 / 层间环，非法即抛）；
    /// 无论其取值，JSON 结构校验（rootLevelId / levelId / childLevelId / containsEdgeType 缺失）总是执行。
    /// </summary>
    public static HierarchySchema Parse(string json, bool validate = true)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("层次 schema JSON 不可为空。", nameof(json));

        var dto = JsonSerializer.Deserialize<SchemaDto>(json, Options)
                  ?? throw new JsonException("层次 schema JSON 反序列化结果为空。");
        var schema = ToSchema(dto);
        if (validate) HierarchySchemaValidator.ThrowIfInvalid(schema);
        return schema;
    }

    /// <summary>把 schema 序列化为 JSON（声明式往返）。</summary>
    public static string Serialize(HierarchySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return JsonSerializer.Serialize(ToDto(schema), Options);
    }

    private static HierarchySchema ToSchema(SchemaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RootLevelId))
            throw new JsonException("层次 schema JSON 缺少 rootLevelId。");
        var levels = (dto.Levels ?? new List<LevelDto>()).Select(level =>
        {
            if (string.IsNullOrWhiteSpace(level.LevelId))
                throw new JsonException("层次 schema JSON 含缺少 levelId 的层。");
            var contains = (level.Contains ?? new List<ContainmentDto>())
                .Select(c =>
                {
                    if (string.IsNullOrWhiteSpace(c.ChildLevelId))
                        throw new JsonException($"层 '{level.LevelId}' 的包含声明缺少 childLevelId。");
                    if (string.IsNullOrWhiteSpace(c.ContainsEdgeType))
                        throw new JsonException($"层 '{level.LevelId}' → '{c.ChildLevelId}' 缺少 containsEdgeType。");
                    return new HierarchyContainment(c.ChildLevelId!, c.ContainsEdgeType!, c.ParentEdgeType);
                })
                .ToList();
            return new HierarchyLevelSpec(
                level.LevelId!,
                string.IsNullOrWhiteSpace(level.EntityType) ? level.LevelId! : level.EntityType!,
                contains,
                level.ProcessId);
        }).ToList();

        return new HierarchySchema(dto.RootLevelId!, levels);
    }

    private static SchemaDto ToDto(HierarchySchema schema)
        => new()
        {
            RootLevelId = schema.RootLevelId,
            Levels = schema.Levels.Select(level => new LevelDto
            {
                LevelId = level.LevelId,
                EntityType = level.EntityType,
                ProcessId = level.ProcessId,
                Contains = (level.Contains ?? Array.Empty<HierarchyContainment>()).Select(c => new ContainmentDto
                {
                    ChildLevelId = c.ChildLevelId,
                    ContainsEdgeType = c.ContainsEdgeType,
                    ParentEdgeType = c.ParentEdgeType,
                }).ToList(),
            }).ToList(),
        };

    internal sealed class SchemaDto
    {
        public string? RootLevelId { get; set; }

        public List<LevelDto>? Levels { get; set; }
    }

    internal sealed class LevelDto
    {
        public string? LevelId { get; set; }

        public string? EntityType { get; set; }

        public string? ProcessId { get; set; }

        public List<ContainmentDto>? Contains { get; set; }
    }

    internal sealed class ContainmentDto
    {
        public string? ChildLevelId { get; set; }

        public string? ContainsEdgeType { get; set; }

        public string? ParentEdgeType { get; set; }
    }
}
