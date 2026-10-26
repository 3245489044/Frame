
using System.Globalization;
using System.Text.Json;
using Modules.Parsing.Keys;
using ParameterResolver.KeyMapping;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Orchestration.IdManagement;
using WorkFlowMethod.Orchestration.Context.Runtime;
using GraphFramework.Models;

namespace WorkFlowMethod.Skeletons.NodeProcess.KeyMapping
{
    public static class KeyProcessor
    {
        #region 层次化键格式（声明单源，运行期只读）

        /// <summary>层次化键格式声明（唯一来源；显式参数注入，不引入可变进程静态，治 P14）。</summary>
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

        // A1：链序号词法唯一来源 = Modules.Parsing.Keys（声明模板，不是自建正则）。
        private static readonly string[] ChainTemplates =
        {
            "{symbol}_c{index:digits}",
            "{symbol}_chain{index:digits}",
        };

        /// <summary>
        /// 解析 Callkey 尾部的 <c>_c{n}</c> / <c>_chain{n}</c>（0 基序号）与去掉尾缀的基键。
        /// <para>命中两种历史写法；不合模板 ⇒ false（不猜）。</para>
        /// </summary>
        public static bool TryParseChainSuffix(string? key, out int index, out string baseKey)
        {
            index = 0;
            baseKey = key ?? string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (var template in ChainTemplates)
            {
                if (!KeyTemplates.TryParseValues(template, key, out var values))
                {
                    continue;
                }

                if (!values.TryGetValue("index", out var raw) ||
                    !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    continue;
                }

                index = parsed;
                baseKey = values.TryGetValue("symbol", out var symbol) ? symbol : string.Empty;
                return true;
            }

            return false;
        }

        public static string ExtractHierarchyFromNode(NodeConfig node, KeyHierarchyFormat format = null)
        {
            var callkey = node?.basicConfig?.Callkey;
            if (string.IsNullOrEmpty(callkey)) return string.Empty;
            if (!TryParseChainSuffix(callkey, out var number, out _)) return string.Empty;

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
        public static string EnsureChainSuffix(string key, int index)
        {
            if (string.IsNullOrEmpty(key)) return key;

            // A1：命中历史尾缀 ⇒ 以基键重建；否则原样追加（与原 Regex.Replace 语义一致）。
            var baseKey = KeyProcessor.TryParseChainSuffix(key, out _, out var parsedBase)
                ? parsedBase
                : key;
            return baseKey + $"_c{index}";
        }

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
            => KeyProcessor.TryParseChainSuffix(key, out var index, out _) ? index : null;
    }
}
