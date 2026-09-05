#region USINGS
using System;
using System.Collections.Generic;
using System.IO;
using System.Dynamic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Common.GraphMindStudio.Function;
using UniversalMiddleware.Extensions;

#endregion

namespace WorkFlow
{
    using STJ = System.Text.Json;

    public static class WorkflowBuilder
    {
        public static string BuildWorkflow(List<object> nodes, HashSet<string>? quotedFields = null)
        {
            var json = UniversalMiddleware.UniversalMiddlewareEntry.ToJson(nodes);
            quotedFields ??= new HashSet<string> { "Label", "ParameterKeys", "ResultKeys" };

            return DictionaryAlgorithm.JsonKeyConverter.ConvertArrayValuesToQuotedString(
               json,
               quotedFields,
               recursive: true);
        }

        public static List<object> ParseFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<object>();

            var raw = STJ.JsonSerializer.Deserialize<STJ.JsonElement>(json);
            if (raw.ValueKind != STJ.JsonValueKind.Array)
                return new List<object>();

            return raw.EnumerateArray()
                .Select(e => (object)e.ToDictionary())
                .ToList();
        }


        public static List<dynamic> CreateNodesFromJson(string json)
        {
            var objects = ParseFromJson(json);
            return ConvertToNodeInstances(objects);
        }

        public static List<dynamic> CreateNodesFromQuotedJson(string json, HashSet<string>? quotedFields = null)
        {
            var objects = ParseFromQuotedJson(json, quotedFields);
            return ConvertToNodeInstances(objects);
        }

        private static List<dynamic> ConvertToNodeInstances(List<object> objects)
        {
            var nodes = new List<dynamic>();

            foreach (var obj in objects)
            {
                switch (obj)
                {
                    case Dictionary<string, object> dict:
                        var node = CreateNodeFromDictionary(dict);
                        if (node != null) nodes.Add(node);
                        break;
                    case STJ.JsonElement jsonElement:
                        var jsonDict = jsonElement.ToDictionary();
                        var jsonNode = CreateNodeFromDictionary(jsonDict);
                        if (jsonNode != null) nodes.Add(jsonNode);
                        break;
                }
            }

            return nodes;
        }

        private static dynamic? CreateNodeFromDictionary(Dictionary<string, object> dict)
        {
            if (!dict.ContainsKey("Symbol"))
                return null;

            dynamic node = new ExpandoObject();
            var nodeDict = node as IDictionary<string, object>;

            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> nestedDict
                    && (kvp.Key == "FuncPipelineConfig" || kvp.Key == "nextNodeConfig"))
                {
                    nodeDict[kvp.Key] = WorkFlowMethod.Infrastructure.ExpandoObjectHelper.ConvertDictionaryToExpando(nestedDict);
                }
                else
                {
                    nodeDict[kvp.Key] = kvp.Value;
                }
            }

            return node;
        }

        public static List<object> ParseFromQuotedJson(string json, HashSet<string>? quotedFields = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<object>();

            quotedFields ??= new HashSet<string> { "Label", "ParameterKeys", "ResultKeys" };

            var processedJson = ConvertQuotedStringToArrayValues(json, quotedFields);
            return ParseFromJson(processedJson);
        }

        private static string ConvertQuotedStringToArrayValues(string json, HashSet<string> keysToConvert)
        {
            try
            {
                var root = JsonNode.Parse(json);
                if (root == null) return json;

                ProcessQuotedFieldsJsonNode(root, keysToConvert);

                var options = new STJ.JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                return root.ToJsonString(options);
            }
            catch (Exception ex)
            {
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("WorkflowBuilder_ConvertQuotedStringToArrayValues_failed", new { error = ex.Message });
                return json;
            }
        }

        private static void ProcessQuotedFieldsJsonNode(JsonNode? node, HashSet<string> keysToConvert)
        {
            if (node == null) return;

            if (node is JsonObject obj)
            {
                var keys = obj.Select(p => p.Key).ToList();
                foreach (var key in keys)
                {
                    var value = obj[key];
                    if (keysToConvert.Contains(key) && value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                    {
                        var strValue = jsonValue.GetValue<string>();
                        if (!string.IsNullOrEmpty(strValue) && strValue.Contains(","))
                        {
                            var array = strValue.Split(',')
                                .Select(s => s.Trim().Trim('"'))
                                .Select(s => (JsonNode)JsonValue.Create(s)!)
                                .ToArray();
                            var jsonArray = new JsonArray(array);
                            obj[key] = jsonArray;
                        }
                    }
                    else if (value is JsonObject || value is JsonArray)
                    {
                        ProcessQuotedFieldsJsonNode(value, keysToConvert);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    ProcessQuotedFieldsJsonNode(item, keysToConvert);
                }
            }
        }


        public static List<object> LoadFromFile(string filePath, HashSet<string>? quotedFields = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"工作流文件未找到: {filePath}");

            var json = File.ReadAllText(filePath);
            return ParseFromQuotedJson(json, quotedFields);
        }

        public static List<dynamic> LoadNodesFromFile(string filePath, HashSet<string>? quotedFields = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"工作流文件未找到: {filePath}");

            var json = File.ReadAllText(filePath);
            return CreateNodesFromQuotedJson(json, quotedFields);
        }

        public static WorkFlowMethod.Infrastructure.ValidationResult ValidateWorkflow<T>(List<T> nodes) where T : class
        {
            var result = new WorkFlowMethod.Infrastructure.ValidationResult();

            if (nodes == null || nodes.Count == 0)
            {
                result.AddError("工作流节点列表为空");
                return result;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    result.AddError($"第{i}个节点为null");
                    continue;
                }

                switch (node)
                {
                    case STJ.JsonElement jsonElement:
                        if (!jsonElement.TryGetProperty("Symbol", out _))
                            result.AddError($"第{i}个节点缺少Symbol字段");
                        if (!jsonElement.TryGetProperty("NodeId", out _))
                            result.AddError($"第{i}个节点缺少NodeId字段");
                        break;
                    case IDictionary<string, object> nodeDict:
                        if (!nodeDict.ContainsKey("Symbol"))
                            result.AddError($"第{i}个节点缺少Symbol字段");
                        if (!nodeDict.ContainsKey("NodeId"))
                            result.AddError($"第{i}个节点缺少NodeId字段");
                        break;
                }
            }

            return result;
        }

        public static void SaveToFile(List<object> nodes, string filePath, HashSet<string>? quotedFields = null)
        {
            var json = BuildWorkflow(nodes, quotedFields);
            File.WriteAllText(filePath, json);
        }

        public static string BuildSingleNode(
            string symbol,
            string nodeId,
            string funcName,
            object[] parameterKeys,
            object[] resultKeys,
            string? label = null,
            HashSet<string>? quotedFields = null)
        {
            var nodes = new List<object>();
            AddNode(nodes, symbol, nodeId, funcName, parameterKeys, resultKeys, label);
            return BuildWorkflow(nodes, quotedFields);
        }

        public static void AddNodesFromStrings(List<object> nodes, params string[] nodeDefinitions)
        {
            foreach (var definition in nodeDefinitions)
            {
                var parts = definition.Split(':');
                if (parts.Length < 3)
                    continue;

                var symbol = parts[0];
                var nodeId = parts[1];
                var funcName = parts[2];
                object[]? parameterKeys = null;
                object[]? resultKeys = null;
                string? label = null;

                if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
                    parameterKeys = parts[3].Split(',').Select(p => p.Trim()).ToArray<object>();

                if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
                    resultKeys = parts[4].Split(',').Select(p => p.Trim()).ToArray<object>();

                if (parts.Length > 5)
                    label = parts[5];

                AddNode(nodes, symbol, nodeId, funcName, parameterKeys, resultKeys, label);
            }
        }

        public static string BuildFromStrings(params string[] nodeDefinitions)
        {
            var nodes = new List<object>();
            AddNodesFromStrings(nodes, nodeDefinitions);
            return BuildWorkflow(nodes);
        }

        public static string GetSummary(List<object> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return "Empty Workflow";

            var nodeInfos = nodes.Select((n, i) =>
            {
                string symbol = "N/A";
                string nodeId = "N/A";

                switch (n)
                {
                    case STJ.JsonElement jsonElement:
                        if (jsonElement.TryGetProperty("Symbol", out var symEl))
                            symbol = symEl.ToString();
                        if (jsonElement.TryGetProperty("NodeId", out var idEl))
                            nodeId = idEl.ToString();
                        break;
                    case ExpandoObject expando:
                        var dict = expando as IDictionary<string, object>;
                        symbol = dict.ContainsKey("Symbol") ? dict["Symbol"]?.ToString() ?? "N/A" : "N/A";
                        nodeId = dict.ContainsKey("NodeId") ? dict["NodeId"]?.ToString() ?? "N/A" : "N/A";
                        break;
                }

                return $"[{i}] {symbol} ({nodeId})";
            });

            return $"Workflow ({nodes.Count} nodes): {string.Join(" -> ", nodeInfos)}";
        }

        public static void AddNode(
            List<object> nodes,
            string symbol,
            string nodeId,
            string funcName,
            object[]? parameterKeys,
            object[]? resultKeys,
            string? label = null,
            dynamic? nextNodeConfig = null)
        {
            var node = CreateNode(symbol, nodeId, funcName, parameterKeys, resultKeys, label, nextNodeConfig);
            nodes.Add(node);
        }

        public static dynamic CreateNextNodeConfig(Dictionary<string, object>? configs = null)
        {
            dynamic config = new ExpandoObject();
            var configDict = config as IDictionary<string, object>;

            if (configs != null)
            {
                foreach (var kvp in configs)
                    configDict[kvp.Key] = kvp.Value;
            }

            return config;
        }

        public static dynamic CreateNodeConfig(
            string funcName,
            object[] parameterKeys,
            object[] resultKeys,
            Dictionary<string, object>? additionalConfigs = null)
        {
            dynamic config = new ExpandoObject();
            var configDict = config as IDictionary<string, object>;

            config.FuncName = funcName;
            config.ParameterKeys = parameterKeys;
            config.ResultKeys = resultKeys;

            if (additionalConfigs != null)
            {
                foreach (var kvp in additionalConfigs)
                    configDict[kvp.Key] = kvp.Value;
            }

            return config;
        }

        public static dynamic CreateNode(
            string symbol,
            string nodeId,
            string funcName,
            object[] parameterKeys,
            object[] resultKeys,
            string? label = null,
            dynamic? nextNodeConfig = null,
            Dictionary<string, object>? additionalProperties = null)
        {
            dynamic node = new ExpandoObject();
            var nodeDict = node as IDictionary<string, object>;

            node.Symbol = symbol;
            node.Label = label;
            node.NodeId = nodeId;

            dynamic config = new ExpandoObject();
            config.FuncName = funcName;
            config.ParameterKeys = parameterKeys;
            config.ResultKeys = resultKeys;
            node.FuncPipelineConfig = config;

            if (nextNodeConfig != null)
                node.nextNodeConfig = nextNodeConfig;

            if (additionalProperties != null)
            {
                foreach (var kvp in additionalProperties)
                    nodeDict[kvp.Key] = kvp.Value;
            }

            return node;
        }

    }
}

namespace WorkFlowMethod.Execution.Adapters
{
    public static class WorkflowBuilderBridge
    {
        public static TConfig.ExecutionPlan ToShareWorkflow(
            this List<object> nodes,
            string workflowId)
        {
            var workflow = new TConfig.ExecutionPlan
            {
                Id = workflowId,
                Name = workflowId
            };

            foreach (var node in nodes)
            {
                var shareNode = ConvertDynamicNodeToShareNodeDef(node);
                if (shareNode != null)
                {
                    workflow.Steps.Add(shareNode);
                }
            }

            return workflow;
        }

        private static TConfig.StepDefinition? ConvertDynamicNodeToShareNodeDef(object node)
        {
            if (node == null) return null;

            string symbol = string.Empty;
            string nodeId = string.Empty;

            switch (node)
            {
                case System.Text.Json.JsonElement jsonElement:
                    if (jsonElement.TryGetProperty("Symbol", out var symEl))
                        symbol = symEl.GetString() ?? string.Empty;
                    if (jsonElement.TryGetProperty("NodeId", out var idEl))
                        nodeId = idEl.GetString() ?? string.Empty;
                    break;
                case System.Dynamic.ExpandoObject expando:
                    var dict = (IDictionary<string, object>)expando;
                    if (dict.TryGetValue("Symbol", out var sym))
                        symbol = sym?.ToString() ?? string.Empty;
                    if (dict.TryGetValue("NodeId", out var id))
                        nodeId = id?.ToString() ?? string.Empty;
                    break;
                default:
                    return null;
            }

            if (string.IsNullOrEmpty(nodeId)) return null;

            return new TConfig.StepDefinition
            {
                Id = nodeId,
                Name = symbol,
                StepType = "Function",
                IsEnabled = true,
                IsSkipped = false
            };
        }

        public static List<object> ToOperationNodes(
            this TConfig.ExecutionPlan workflow)
        {
            var nodes = new List<object>();

            foreach (var node in workflow.Steps)
            {
                var dict = new Dictionary<string, object>
                {
                    ["Symbol"] = node.Name,
                    ["NodeId"] = node.Id,
                    ["Label"] = node.Name
                };

                var configDict = new Dictionary<string, object>
                {
                    ["FuncName"] = node.Name
                };

                if (node.Metadata.TryGetValue("FunctionName", out var funcName))
                {
                    configDict["FuncName"] = funcName;
                }

                dict["FuncPipelineConfig"] = configDict;
                nodes.Add(dict);
            }

            return nodes;
        }
    }
}
