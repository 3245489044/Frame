using System;
using System.Collections.Generic;
using System.Linq;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Builtin;

/// <summary>
/// 内置层 / 包含边的标识常量（示例词汇，非框架枚举）——框架本身不规定任何具体层名，
/// 此处仅为"开箱即用"提供一套与既有 nodes/task/history 三段一致的示例词汇。
/// </summary>
public static class BuiltinHierarchyLevels
{
    /// <summary>历史层（顶层容器）。</summary>
    public const string History = "history";

    /// <summary>任务层。</summary>
    public const string Task = "task";

    /// <summary>节点层（叶）。</summary>
    public const string Node = "node";

    /// <summary>历史 → 任务 包含边。</summary>
    public const string HistoryOwnsTask = "history_owns_task";

    /// <summary>任务 → 节点 包含边。</summary>
    public const string CrossDbLink = "cross_db_link";

    /// <summary>任务 → 子任务 包含边（自递归层用）。</summary>
    public const string TaskOwnsTask = "task_owns_task";
}

/// <summary>
/// 内置层次 schema 示例（<b>非框架约束</b>）：
/// <list type="bullet">
///   <item><see cref="HistoryTaskNode"/>：固定 3 层异构（history → task → node）；</item>
///   <item><see cref="TaskTree"/>：自递归不定深度（task 自包含 + node 叶）。</item>
/// </list>
/// 二者证明同一引擎既能跑"异构 N 层"也能跑"同构不定深度"——差异仅在 schema 数据。
/// </summary>
public static class BuiltinHierarchySchema
{
    /// <summary>内置 3 层异构 schema：history → task → node（node 为叶）。</summary>
    public static HierarchySchema HistoryTaskNode { get; } = new(
        BuiltinHierarchyLevels.History,
        new[]
        {
            new HierarchyLevelSpec(
                BuiltinHierarchyLevels.History,
                EntityType: "history",
                Contains: new[]
                {
                    new HierarchyContainment(BuiltinHierarchyLevels.Task, BuiltinHierarchyLevels.HistoryOwnsTask),
                }),
            new HierarchyLevelSpec(
                BuiltinHierarchyLevels.Task,
                EntityType: "task",
                Contains: new[]
                {
                    new HierarchyContainment(BuiltinHierarchyLevels.Node, BuiltinHierarchyLevels.CrossDbLink),
                }),
            HierarchyLevelSpec.Leaf(BuiltinHierarchyLevels.Node, entityType: "node"),
        });

    /// <summary>自递归 schema：task 自包含（不定深度）+ node 叶。</summary>
    public static HierarchySchema TaskTree { get; } = new(
        BuiltinHierarchyLevels.Task,
        new[]
        {
            new HierarchyLevelSpec(
                BuiltinHierarchyLevels.Task,
                EntityType: "task",
                Contains: new[]
                {
                    new HierarchyContainment(BuiltinHierarchyLevels.Task, BuiltinHierarchyLevels.TaskOwnsTask),
                    new HierarchyContainment(BuiltinHierarchyLevels.Node, BuiltinHierarchyLevels.CrossDbLink),
                }),
            HierarchyLevelSpec.Leaf(BuiltinHierarchyLevels.Node, entityType: "node"),
        });
}

/// <summary>
/// 层次 schema 流式构造器（便于声明式装配与单测；层可任意顺序声明，包含边自动补层占位）。
/// </summary>
public sealed class HierarchySchemaBuilder
{
    private readonly string _rootLevelId;
    private readonly Dictionary<string, LevelDraft> _levels = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    private HierarchySchemaBuilder(string rootLevelId)
    {
        if (string.IsNullOrWhiteSpace(rootLevelId))
            throw new ArgumentException("根层 Id 不可为空。", nameof(rootLevelId));
        _rootLevelId = rootLevelId;
        Ensure(rootLevelId);
    }

    /// <summary>以指定根层开启构造。</summary>
    public static HierarchySchemaBuilder Root(string rootLevelId) => new(rootLevelId);

    /// <summary>声明 / 补全一个层（实体类型 + 可选默认处理器）。</summary>
    public HierarchySchemaBuilder Level(string levelId, string entityType, string? processId = null)
    {
        var draft = Ensure(levelId);
        draft.EntityType = entityType;
        draft.ProcessId = processId;
        return this;
    }

    /// <summary>声明一条包含关系；两端层若未声明则自动以层名占位（可稍后经 <see cref="Level"/> 补实体类型）。</summary>
    public HierarchySchemaBuilder Contains(string parentLevelId, string childLevelId, string containsEdgeType, string? parentEdgeType = null)
    {
        if (string.IsNullOrWhiteSpace(containsEdgeType))
            throw new ArgumentException("包含边类型不可为空。", nameof(containsEdgeType));
        var parent = Ensure(parentLevelId);
        Ensure(childLevelId);
        parent.Contains.Add(new HierarchyContainment(childLevelId, containsEdgeType, parentEdgeType));
        return this;
    }

    /// <summary>构造 schema（不做完整性校验；需要时调用 <see cref="HierarchySchemaValidator.ThrowIfInvalid"/>）。</summary>
    public HierarchySchema Build()
        => new(_rootLevelId, _order.Select(id => new HierarchyLevelSpec(
            id,
            _levels[id].EntityType,
            _levels[id].Contains.ToList(),
            _levels[id].ProcessId)).ToList());

    /// <summary>构造并校验 schema（非法即抛）。</summary>
    public HierarchySchema BuildValidated()
    {
        var schema = Build();
        HierarchySchemaValidator.ThrowIfInvalid(schema);
        return schema;
    }

    private LevelDraft Ensure(string levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("层 Id 不可为空。", nameof(levelId));
        if (_levels.TryGetValue(levelId, out var existing)) return existing;
        var draft = new LevelDraft(levelId);
        _levels[levelId] = draft;
        _order.Add(levelId);
        return draft;
    }

    private sealed class LevelDraft(string entityType)
    {
        public string EntityType { get; set; } = entityType;

        public string? ProcessId { get; set; }

        public List<HierarchyContainment> Contains { get; } = new();
    }
}
