#nullable enable

using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Reference
{
    public static class ReferenceConfig
    {
        public static ReferenceConfiguration GenerateConfiguration()
        {
            return new ReferenceConfiguration
            {
                Name = "default",
                Description = "默认引用管理配置 - 完整实现当前引用功能",
                Schema = GenerateSchema(),
                Storage = GenerateStorage(),
                Cache = GenerateCache(),
                Validation = GenerateValidation()
            };
        }

        private static Models.ReferenceSchema GenerateSchema()
        {
            return new Models.ReferenceSchema
            {
                NodeTypes = new System.Collections.Generic.List<Models.NodeType>
                {
                    new() { Type = "workflow", DisplayName = "工作流", Icon = "workflow", Color = "#4CAF50", Visible = true, SortWeight = 1, DisplayProperties = new System.Collections.Generic.List<string> { "name", "status", "version" } },
                    new() { Type = "module", DisplayName = "模块", Icon = "module", Color = "#2196F3", Visible = true, SortWeight = 2, DisplayProperties = new System.Collections.Generic.List<string> { "name", "type", "version" } },
                    new() { Type = "service", DisplayName = "服务", Icon = "service", Color = "#FF9800", Visible = true, SortWeight = 3, DisplayProperties = new System.Collections.Generic.List<string> { "name", "endpoint", "status" } },
                    new() { Type = "repository", DisplayName = "仓库", Icon = "repository", Color = "#b02787", Visible = true, SortWeight = 4, DisplayProperties = new System.Collections.Generic.List<string> { "name", "url", "branch" } },
                    new() { Type = "model", DisplayName = "模型", Icon = "model", Color = "#F44336", Visible = true, SortWeight = 5, DisplayProperties = new System.Collections.Generic.List<string> { "name", "version", "type" } },
                    new() { Type = "data", DisplayName = "数据", Icon = "data", Color = "#00BCD4", Visible = true, SortWeight = 6, DisplayProperties = new System.Collections.Generic.List<string> { "name", "type", "size" } },
                    new() { Type = "code", DisplayName = "代码", Icon = "code", Color = "#607D8B", Visible = true, SortWeight = 7, DisplayProperties = new System.Collections.Generic.List<string> { "name", "language", "path" } },
                    new() { Type = "tool", DisplayName = "工具", Icon = "tool", Color = "#FFC107", Visible = true, SortWeight = 8, DisplayProperties = new System.Collections.Generic.List<string> { "name", "version", "type" } },
                    new() { Type = "api", DisplayName = "API", Icon = "api", Color = "#795548", Visible = true, SortWeight = 9, DisplayProperties = new System.Collections.Generic.List<string> { "name", "endpoint", "method" } },
                    new() { Type = "database", DisplayName = "数据库", Icon = "database", Color = "#4CAF50", Visible = true, SortWeight = 10, DisplayProperties = new System.Collections.Generic.List<string> { "name", "type", "connection" } }
                },
                EdgeTypes = new System.Collections.Generic.List<Models.EdgeType>
                {
                    new() { Type = "depends_on", DisplayName = "依赖", Color = "#666666", LineStyle = "solid", Visible = true },
                    new() { Type = "contains", DisplayName = "包含", Color = "#888888", LineStyle = "dashed", Visible = true },
                    new() { Type = "uses", DisplayName = "使用", Color = "#AAAAAA", LineStyle = "dotted", Visible = true },
                    new() { Type = "calls", DisplayName = "调用", Color = "#3498db", LineStyle = "solid", Visible = true },
                    new() { Type = "generates", DisplayName = "生成", Color = "#27ae60", LineStyle = "dashed", Visible = true }
                },
                Filters = new System.Collections.Generic.List<Models.FilterRule>
                {
                    new() { Type = Models.FilterType.Node, TargetType = "workflow", Condition = "status", Value = "active" },
                    new() { Type = Models.FilterType.Edge, TargetType = "depends_on", Condition = "strength", Value = 0.5 }
                }
            };
        }

        private static Models.StorageConfiguration GenerateStorage()
        {
            return new Models.StorageConfiguration
            {
                Type = "inmemory",
                ConnectionString = "",
                Timeout = 30000,
                BatchSize = 1000
            };
        }

        private static Models.CacheConfiguration GenerateCache()
        {
            return new Models.CacheConfiguration
            {
                Enabled = true,
                ExpirationMinutes = 5,
                MaxSize = 1000
            };
        }

        private static Models.ValidationConfiguration GenerateValidation()
        {
            return new Models.ValidationConfiguration
            {
                Enabled = true,
                Level = "Module",
                Rules = new System.Collections.Generic.List<Models.ValidationRule>
                {
                    new()
                    {
                        Name = "workflow_completeness",
                        Description = "工作流必须有开始和结束节点",
                        Type = "workflow_validation",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "requireStartNode", true },
                            { "requireEndNode", true },
                            { "minNodes", 2 },
                            { "maxNodes", 100 }
                        }
                    },
                    new()
                    {
                        Name = "service_dependencies",
                        Description = "服务必须有依赖的模块",
                        Type = "dependency_validation",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "requiredDependencies", new System.Collections.Generic.List<string> { "module" } },
                            { "minDependencies", 1 }
                        }
                    },
                    new()
                    {
                        Name = "model_data_source",
                        Description = "模型必须有数据来源",
                        Type = "data_validation",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "requireDataSource", true },
                            { "allowedDataTypes", new System.Collections.Generic.List<string> { "data", "database" } }
                        }
                    },
                    new()
                    {
                        Name = "reference_integrity",
                        Description = "引用关系必须完整",
                        Type = "integrity_validation",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "checkDuplicates", true },
                            { "checkOrphans", true },
                            { "checkCycles", true }
                        }
                    }
                }
            };
        }

        public static void AddNodeType(Models.NodeType nodeType, ReferenceConfiguration? config = null)
        {
            var cfg = config ?? GenerateConfiguration();
            cfg.Schema.NodeTypes.Add(nodeType);
        }

        public static void AddEdgeType(Models.EdgeType edgeType, ReferenceConfiguration? config = null)
        {
            var cfg = config ?? GenerateConfiguration();
            cfg.Schema.EdgeTypes.Add(edgeType);
        }

        public static void AddValidationRule(Models.ValidationRule rule, ReferenceConfiguration? config = null)
        {
            var cfg = config ?? GenerateConfiguration();
            cfg.Validation.Rules.Add(rule);
        }
    }
}
