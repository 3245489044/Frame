
using System.Text.Json;
using ParameterResolver.KeyMapping;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using System.Text.RegularExpressions;
using WorkFlowMethod.Orchestration.IdManagement;
using WorkFlowMethod.Orchestration.Context.Runtime;
using GraphFramework.Models;

namespace WorkFlowMethod.Skeletons.NodeProcess.KeyMapping
{
    public static class KeyProcessor
    {
        #region 层次化键格式（声明单源，运行期只读）

        /// <summary>层次化键格式声明（唯一来源；注入 = 传参覆盖，不引入可变进程静态，治 P14）。</summary>
        public static KeyHierarchyFormat HierarchyFormat { get; } = KeyHierarchyFormat.CompatibleDefault;

        public static string HierarchyNumberFormat => HierarchyFormat.NumberFormat;

        public static string HierarchyPrefix => HierarchyFormat.Prefix;

        public static string HierarchySuffix => HierarchyFormat.Suffix;

        public static string HierarchySeparator => HierarchyFormat.Separator;

        #endregion

        #region 键映射器实例（延迟初始化）

        private static readonly Lazy<KeyMapperBase<string>> _mapper = new(() => new KeyMapperBaseImpl());

        public static KeyMapperBase<string> Mapper => _mapper.Value;

        #endregion

        #region 主要方法

        public static void ProcessConnectionKeyMapping(
            WorkflowConnection<string, string> connection,
            string keyMappingJson,
            List<Chain<string, NodeConfig>> workflows)
        {
            if (string.IsNullOrEmpty(keyMappingJson) || connection == null) return;

            try
            {
                var fromNode = FindNodeByCallkey(workflows, connection.FromNode);
                var toNode = FindNodeByCallkey(workflows, connection.ToNode);
                var mappingConfig = DeserializeConfig(keyMappingJson);

                var sourceKeys = GetNormalizedKeys(fromNode.funcPipelineConfig?.ResultKeys, mappingConfig);
                var targetKeys = GetNormalizedKeys(toNode.funcPipelineConfig?.ParameterKeys, mappingConfig);
                if (targetKeys.Count == 0) throw new InvalidOperationException("目标键为空");

                var (updatedKeys, _) = ProcessKeyMappings(sourceKeys, targetKeys, mappingConfig);
                toNode.funcPipelineConfig.ParameterKeys = JsonSerializer.Serialize(updatedKeys);
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"处理键映射时发生错误: {ex.Message}", ex);
            }
        }

        public static void ProcessNodeKeys(NodeConfig node, string hierarchy = null, KeyHierarchyFormat format = null)
        {
            if (node?.funcPipelineConfig == null) return;
            var effectiveFormat = format ?? HierarchyFormat;
            hierarchy ??= ExtractHierarchyFromNode(node, effectiveFormat);
            if (string.IsNullOrEmpty(hierarchy)) return;

            var formattedHierarchy = FormatHierarchy(hierarchy, effectiveFormat);
            node.funcPipelineConfig.ParameterKeys = AddHierarchyToKeysJson(
                node.funcPipelineConfig.ParameterKeys, formattedHierarchy);
            node.funcPipelineConfig.ResultKeys = AddHierarchyToKeysJson(
                node.funcPipelineConfig.ResultKeys, formattedHierarchy);
        }

        public static List<string> ParseKeysFromJson(string keysJson)
        {
            if (string.IsNullOrEmpty(keysJson)) return new List<string>();
            return Mapper.ParseKeysFromJson(keysJson).ToList();
        }

        #endregion

        #region 键映射处理（委托给 KeyMapperBase）

        private static (List<string> updatedKeys, List<KeyMatchResult> matchResults)
            ProcessKeyMappings(List<string> sourceKeys, List<string> targetKeys, KeyMappingConfig config)
        {
            var (updatedKeys, matchResults) = Mapper.MapBatch(sourceKeys, targetKeys, config);
            return (updatedKeys.ToList(), matchResults.ToList());
        }

        #endregion

        #region 辅助方法

        private static List<string> GetNormalizedKeys(string keysJson, KeyMappingConfig config)
        {
            var keys = ParseKeysFromJson(keysJson);
            if (!config.EnableHierarchyNormalization) return keys;
            return keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
        }

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static T Deserialize<T>(string json) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(json, DeserializeOptions); }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Log("KeyProcessor_DeserializeError", new
                {
                    type = typeof(T).FullName,
                    error = ex.Message
                });
                return null;
            }
        }

        private static KeyMappingConfig DeserializeConfig(string json) =>
            Deserialize<KeyMappingConfig>(json);

        #endregion

        #region 层次化处理

        public static string ExtractHierarchyFromNode(NodeConfig node, KeyHierarchyFormat format = null)
        {
            var callkey = node?.basicConfig?.Callkey;
            if (string.IsNullOrEmpty(callkey)) return string.Empty;

            var match = Regex.Match(callkey, @"\b_c(\d+)$|\b_chain(\d+)$");
            if (!match.Success) return string.Empty;

            int number = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            return FormatHierarchyNumber(number + 1, format ?? HierarchyFormat);
        }

        private static string FormatHierarchyNumber(int number, KeyHierarchyFormat format)
        {
            if (string.IsNullOrEmpty(format.NumberFormat)) return number.ToString();
            if (format.NumberFormat.StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                int padding = format.NumberFormat.Length > 1
                    && int.TryParse(format.NumberFormat.AsSpan(1), out int specifiedPadding)
                    ? Math.Max(1, specifiedPadding) : 1;
                return number.ToString($"D{padding}");
            }
            return number.ToString(format.NumberFormat);
        }

        private static string FormatHierarchy(string hierarchy, KeyHierarchyFormat format)
        {
            if (string.IsNullOrEmpty(hierarchy)) return hierarchy;
            if (!string.IsNullOrEmpty(format.Separator))
            {
                var prefix = !string.IsNullOrEmpty(format.Prefix) ? $"{format.Prefix}{format.Separator}" : "";
                var suffix = !string.IsNullOrEmpty(format.Suffix) ? $"{format.Separator}{format.Suffix}" : "";
                return $"{prefix}{hierarchy}{suffix}";
            }
            return $"{format.Prefix}{hierarchy}{format.Suffix}";
        }

        public static string AddHierarchyToKey(string key, string hierarchy) =>
            string.IsNullOrEmpty(key) || string.IsNullOrEmpty(hierarchy)
                ? key
                : PathProcessor.PathBuilder.CombinePath(key, hierarchy);

        public static List<string> AddHierarchyToKeys(List<string> keys, string hierarchy) =>
            keys == null || keys.Count == 0 || string.IsNullOrEmpty(hierarchy)
                ? keys ?? new List<string>()
                : keys.Select(key => AddHierarchyToKey(key, hierarchy)).ToList();

        public static string AddHierarchyToKeysJson(string keysJson, string hierarchy)
        {
            if (string.IsNullOrEmpty(keysJson) || string.IsNullOrEmpty(hierarchy)) return keysJson;
            var keys = ParseKeysFromJson(keysJson);
            var processedKeys = AddHierarchyToKeys(keys, hierarchy);
            return JsonSerializer.Serialize(processedKeys);
        }

        #endregion

        #region 节点查找

        private static NodeConfig FindNodeByCallkey(List<Chain<string, NodeConfig>> workflows, string callkey)
        {
            foreach (var workflow in workflows)
            {
                foreach (var node in workflow.AllNodes)
                {
                    if (node.basicConfig?.Callkey == callkey)
                        return node;
                }
            }
            throw new KeyNotFoundException($"未找到Callkey为'{callkey}'的节点");
        }

        #endregion
    }


    public class KeyMapperBaseImpl : KeyMapperBase<string>
    {
        protected override string TrimKey(string key) => key?.Trim() ?? string.Empty;

        protected override bool IsEmptyKey(string key) => string.IsNullOrWhiteSpace(key);
    }


    public static class KeyManager
    {
        public static string EnsureChainSuffix(string key, int index) =>
            string.IsNullOrEmpty(key)
                ? key
                : Regex.Replace(key, @"_c\d+$|_chain\d+$", "") + $"_c{index}";

        public static void ProcessKeyMapping(
            WorkflowConnection<string, string> connection,
            string keyMapping,
            List<Chain<string, NodeConfig>> workflows) =>
            KeyProcessor.ProcessConnectionKeyMapping(connection, keyMapping, workflows);

        public static string GenerateCallkey(
            string symbol,
            int chainIndex,
            int nodeIndex,
            WorkflowRuntimeContext context = null,
            string pattern = null)
        {
            var effectivePattern = pattern ?? IdConstants.DefaultNodeCallkeyPattern;
            return effectivePattern
                .Replace("{symbol}", symbol)
                .Replace("{chainIndex}", chainIndex.ToString())
                .Replace("{nodeIndex}", nodeIndex.ToString());
        }

        public static string GenerateReference(
            string reference,
            int chainIndex,
            WorkflowRuntimeContext context = null,
            string pattern = null)
        {
            var effectivePattern = pattern ?? IdConstants.DefaultNodeCallkeyPattern;
            return effectivePattern
                .Replace("{symbol}", reference)
                .Replace("{chainIndex}", chainIndex.ToString())
                .Replace("{nodeIndex}", "0");
        }

        public static int? ExtractChainIndex(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var match = Regex.Match(key, @"_(?:c|chain)(\d+)$");
            return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
        }
    }
}
