using Modules.DeclarativeDataflow.Builtin;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Serialization;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow;

/// <summary>
/// 三层（history → task → node）层次 schema 的**唯一声明点** —— 层数/层名/包含关系全部外置为数据，
/// 由 Modules.DeclarativeDataflow 的引擎解释（层次是数据不是类型，见框架 README）。
/// <para>
/// 包含边类型直接引用 <see cref="CrossDbReferenceTypes"/> 常量（图侧唯一权威），不复制字符串字面量；
/// 每层 <c>ProcessId</c> 对应 <c>LevelProcessors</c> 中各处理器的 Id。
/// </para>
/// </summary>
public static class HierarchySchemas
{
    /// <summary>历史层 Id（History = Task 上层 class 容器）。</summary>
    public const string HistoryLevel = "history";

    /// <summary>任务层 Id。</summary>
    public const string TaskLevel = "task";

    /// <summary>节点层 Id（叶）。</summary>
    public const string NodeLevel = "node";

    /// <summary>历史层默认处理器 Id（历史运行态概览）。</summary>
    public const string HistoryProcessId = "history-overview";

    /// <summary>任务层默认处理器 Id（任务运行状态投影）。</summary>
    public const string TaskProcessId = "task-status";

    /// <summary>节点层默认处理器 Id（节点输出端口投影）。</summary>
    public const string NodeProcessId = "node-output";

    private static readonly HierarchySchema HistoryTaskNodeSchema = HierarchySchemaBuilder
        .Root(HistoryLevel)
        .Level(HistoryLevel, HistoryLevel, HistoryProcessId)
        .Level(TaskLevel, TaskLevel, TaskProcessId)
        .Level(NodeLevel, NodeLevel, NodeProcessId)
        .Contains(HistoryLevel, TaskLevel, CrossDbReferenceTypes.HistoryOwnsTask)
        .Contains(TaskLevel, NodeLevel, CrossDbReferenceTypes.CrossDbLink)
        .BuildValidated();

    /// <summary>标准三层异构 schema：history → task → node（node 为叶）。</summary>
    public static HierarchySchema HistoryTaskNode => HistoryTaskNodeSchema;

    /// <summary>
    /// 任务自递归 schema（task 自包含 = 不定深度 sub-task，node 为叶），装载期即校验层图合法性。
    /// <para>图侧须已为 <paramref name="subtaskEdgeType"/> 建立写路径；未建立时子层查询恒为空集，
    /// 引擎按容器层透传（不报错、不阻断）。</para>
    /// </summary>
    public static HierarchySchema TaskTree(string subtaskEdgeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subtaskEdgeType);
        return HierarchySchemaBuilder
            .Root(TaskLevel)
            .Level(TaskLevel, TaskLevel, TaskProcessId)
            .Level(NodeLevel, NodeLevel, NodeProcessId)
            .Contains(TaskLevel, TaskLevel, subtaskEdgeType)
            .Contains(TaskLevel, NodeLevel, CrossDbReferenceTypes.CrossDbLink)
            .BuildValidated();
    }

    /// <summary>
    /// 标准三层 schema 的**声明式 JSON 形态**（"层次是数据"的落地载体）——与代码默认值 <see cref="HistoryTaskNode"/> 语义等价。
    /// <para>
    /// 层名 / 处理器 Id 经类内常量、包含边类型经 <see cref="CrossDbReferenceTypes"/> 常量插值，与图侧单一权威同源；
    /// 消费方可将其落入配置后经 <see cref="LoadOrDefault"/> 装载，从而在不改代码的前提下调整层次。
    /// </para>
    /// </summary>
    public static readonly string HistoryTaskNodeJson = $$"""
        {
          "rootLevelId": "{{HistoryLevel}}",
          "levels": [
            {
              "levelId": "{{HistoryLevel}}",
              "entityType": "{{HistoryLevel}}",
              "processId": "{{HistoryProcessId}}",
              "contains": [
                { "childLevelId": "{{TaskLevel}}", "containsEdgeType": "{{CrossDbReferenceTypes.HistoryOwnsTask}}" }
              ]
            },
            {
              "levelId": "{{TaskLevel}}",
              "entityType": "{{TaskLevel}}",
              "processId": "{{TaskProcessId}}",
              "contains": [
                { "childLevelId": "{{NodeLevel}}", "containsEdgeType": "{{CrossDbReferenceTypes.CrossDbLink}}" }
              ]
            },
            {
              "levelId": "{{NodeLevel}}",
              "entityType": "{{NodeLevel}}",
              "processId": "{{NodeProcessId}}",
              "contains": []
            }
          ]
        }
        """;

    /// <summary>任务自递归 schema 的**声明式 JSON 形态**（自环 = 不定深度；遍历时必须配展开预算）。</summary>
    /// <param name="subtaskEdgeType">task → sub-task 归属边类型（如 <see cref="CrossDbReferenceTypes.TaskOwnsTask"/>）。</param>
    public static string TaskTreeJson(string subtaskEdgeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subtaskEdgeType);
        return $$"""
            {
              "rootLevelId": "{{TaskLevel}}",
              "levels": [
                {
                  "levelId": "{{TaskLevel}}",
                  "entityType": "{{TaskLevel}}",
                  "processId": "{{TaskProcessId}}",
                  "contains": [
                    { "childLevelId": "{{TaskLevel}}", "containsEdgeType": "{{subtaskEdgeType}}" },
                    { "childLevelId": "{{NodeLevel}}", "containsEdgeType": "{{CrossDbReferenceTypes.CrossDbLink}}" }
                  ]
                },
                {
                  "levelId": "{{NodeLevel}}",
                  "entityType": "{{NodeLevel}}",
                  "processId": "{{NodeProcessId}}",
                  "contains": []
                }
              ]
            }
            """;
    }

    /// <summary>从 JSON 声明装载层次 schema（结构 + 语义完整性校验；非法即抛）。</summary>
    public static HierarchySchema FromJson(string json) => HierarchySchemaJson.Parse(json, validate: true);

    /// <summary>序列化内置三层 schema（供生成配置文件 / 往返断言）。</summary>
    public static string SerializeDefault() => HierarchySchemaJson.Serialize(HistoryTaskNode);

    /// <summary>
    /// 配置优先装载：<paramref name="json"/> 为空 → 内置三层默认（无诊断）；非空但非法 → 回退内置默认并给出诊断。
    /// <para>回退而非抛出，保证配置问题不阻断确定性路径（对齐「未装配返回诊断」模式）。</para>
    /// </summary>
    public static HierarchySchema LoadOrDefault(string? json, out string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostic = null;
            return HistoryTaskNode;
        }

        try
        {
            var schema = HierarchySchemaJson.Parse(json, validate: true);
            diagnostic = null;
            return schema;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return HistoryTaskNode;
        }
    }
}
