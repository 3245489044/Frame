#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using Modules.Transformers;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using ValidationResult = Share.LogicComposition.Models.ValidationResult;
#endregion

namespace WorkFlowMethod.Orchestration.Config
{
    public static class ModularValidator
    {
        public static DefaultCompositionValidator CreateValidator()
        {
            var rules = new List<Validator<CompositionValidationContext>>
            {
                new("ContextNotNull", ctx => ctx != null, "验证上下文不能为 null"),
                new("OperationTypeSpecified", ctx => ctx != null && !string.IsNullOrWhiteSpace(ctx.OperationType), "操作类型未指定"),

                new("WorkflowChainNotEmpty", ctx =>
                {
                    if (ctx.CustomRules.TryGetValue("Chain<string, NodeConfig>", out var chainObj))
                    {
                        if (chainObj is Chain<string, NodeConfig> chain)
                            return chain.AllNodes != null && chain.AllNodes.Count > 0;
                    }
                    return true;
                }, "工作流链为空"),

                new("ConnectionPositionValid", ctx =>
                {
                    if (ctx.CustomRules.TryGetValue("ConnectionPosition", out var posObj))
                    {
                        if (posObj is ConnectionPositionInfo<string> pos)
                            return pos.InsertAfterNodeCallkey != null || pos.InsertBeforeNodeCallkey != null;
                    }
                    return true;
                }, "插入位置信息无效，需要指定 InsertAfterNodeCallkey 或 InsertBeforeNodeCallkey"),

                new("ChainConfigNotNull", ctx =>
                {
                    if (ctx.CustomRules.TryGetValue("ChainConfig", out var configObj))
                    {
                        if (configObj is ChainConfig config)
                            return config != null && config.Chains != null && config.Chains.Count > 0;
                    }
                    return true;
                }, "链配置为空"),
            };

            return new DefaultCompositionValidator(rules);
        }

        private static void ValidateNotEmpty<T>(IEnumerable<T> collection, string paramName)
        {
            var list = collection?.ToList() ?? new List<T>();
            global::GraphFramework.Helpers.Guard.AgainstNullOrEmpty(list, paramName);
        }

        public static class WorkflowValidator
        {
            public static ValidationResult ValidateWorkflowChain(Chain<string, NodeConfig> chain)
            {
                var validator = CreateValidator();
                var ctx = new CompositionValidationContext
                {
                    OperationType = "WorkflowValidation",
                    SourceDescription = "WorkflowChain",
                    TargetDescription = "Chain<string, NodeConfig>",
                    CustomRules = new Dictionary<string, object> { ["Chain<string, NodeConfig>"] = chain }
                };
                return validator.Validate(ctx);
            }

            public static void ValidateNodeListNotEmpty(List<NodeConfig> nodes) =>
                ValidateNotEmpty(nodes, nameof(nodes));

            public static void ValidateNodeExists(string callkey, Dictionary<string, NodeConfig> nodeCache)
            {
                global::GraphFramework.Helpers.Guard.AgainstNullOrEmpty(callkey, nameof(callkey));
                global::GraphFramework.Helpers.Guard.AgainstNull(nodeCache, nameof(nodeCache));
                if (!nodeCache.ContainsKey(callkey))
                    throw new ArgumentException($"未找到节点: {callkey}");
            }
        }

        public static class ConnectionValidator
        {
            public static ValidationResult ValidateWorkflowConnections(List<WorkflowConnection<string, string>> connections)
            {
                var validator = CreateValidator();
                var ctx = new CompositionValidationContext
                {
                    OperationType = "ConnectionValidation",
                    SourceDescription = "WorkflowConnections",
                    TargetDescription = "ConnectionList",
                    CustomRules = new Dictionary<string, object>
                    {
                        ["Connections"] = connections ?? new List<WorkflowConnection<string, string>>()
                    }
                };
                return validator.Validate(ctx);
            }

            public static ValidationResult ValidateInsertOperation(ConnectionPositionInfo<string> pos)
            {
                var validator = CreateValidator();
                var ctx = new CompositionValidationContext
                {
                    OperationType = "InsertOperation",
                    SourceDescription = "InsertPosition",
                    TargetDescription = "ConnectionPositionInfo",
                    CustomRules = new Dictionary<string, object> { ["ConnectionPosition"] = pos }
                };
                return validator.Validate(ctx);
            }

            public static void ValidateChainConnectivity(Chain<string, NodeConfig> chain)
            {
                ValidateNotEmpty(chain.AllNodes, nameof(chain.AllNodes));
                var nodeMap = chain.AllNodes
                    .Where(n => n?.basicConfig?.Callkey != null)
                    .ToDictionary(n => n.basicConfig.Callkey);
                if (nodeMap.Count == 0)
                    throw new InvalidOperationException("链中没有有效节点");

                var visited = new HashSet<string>();
                var cur = chain.StartNode;
                while (cur != null)
                {
                    if (cur.basicConfig?.Callkey == null)
                    {
                        cur = GetNextNode(cur, nodeMap);
                        continue;
                    }

                    var key = cur.basicConfig.Callkey;
                    if (!visited.Add(key))
                        throw new InvalidOperationException($"检测到循环链接: {key}");

                    var nextKey = cur.nextNodeConfig?.NextNodeKey;
                    if (!string.IsNullOrEmpty(nextKey) && !nodeMap.ContainsKey(nextKey))
                        throw new InvalidOperationException($"节点的 NextNodeKey '{nextKey}' 指向不存在的节点");

                    cur = GetNextNode(cur, nodeMap);
                }
            }

            private static NodeConfig? GetNextNode(NodeConfig cur, Dictionary<string, NodeConfig> nodeMap)
            {
                var nextKey = cur.nextNodeConfig?.NextNodeKey;
                return !string.IsNullOrEmpty(nextKey) && nodeMap.TryGetValue(nextKey, out var next) ? next : null;
            }
        }

        public static class ConfigValidator
        {
            public static ValidationResult ValidateChainConfig(ChainConfig config)
            {
                var validator = CreateValidator();
                var ctx = new CompositionValidationContext
                {
                    OperationType = "ConfigValidation",
                    SourceDescription = "ChainConfig",
                    TargetDescription = "Config",
                    CustomRules = new Dictionary<string, object> { ["ChainConfig"] = config }
                };
                return validator.Validate(ctx);
            }
        }
    }
}

