#region USINGS
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Linq.Dynamic.Core;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
#endregion
#nullable disable


namespace DictionaryAlgorithm
{
    #region 嵌套字典框架系统

    #region 核心数据结构

    public class NodeInfoBase
    {
        public string Name => Key;

        public string Key { get; set; }

        public string FullPath { get; set; }

        public object Value { get; set; }

        public int Depth { get; set; }

        public bool IsDictionary => Value is Dictionary<string, object>;

        public bool IsArray => Value is List<object>;

        public bool IsLeaf => !IsDictionary && !IsArray;

        public string ParentPath => PathAlgorithm.GetParentPath(FullPath);

        public NodeInfoBase(string key, string fullPath, object value, int depth)
        {
            Key = key;
            FullPath = fullPath;
            Value = value;
            Depth = depth;
        }
    }

    public class TraversalContext
    {
        public string StartKey { get; set; }

        public int TargetDepth { get; set; } = -1;

        public HashSet<string> KeysToSkip { get; set; } = new();

        public bool CollectStatistics { get; set; } = true;

        public bool PreservePaths { get; set; } = true;

        public bool HandleArrays { get; set; } = true;

        public bool StopAtTargetDepth { get; set; } = false;

        public DepthProcessingMode DepthMode { get; set; } = DepthProcessingMode.Inclusive;

        public bool Validate()
        {
            if (TargetDepth < -1)
            {
                throw new ArgumentException("TargetDepth不能小于-1");
            }

            if (TargetDepth == 0 && StartKey == null)
            {
                return true;
            }

            return true;
        }
    }

    public enum DepthProcessingMode
    {
        Inclusive,

        Exact,

        Exclusive
    }

    public class NodeInfo
    {
        public string Key { get; set; }

        public string FullPath { get; set; }

        public object Value { get; set; }

        public int Depth { get; set; }

        public string ParentPath { get; set; }

        public bool IsLeaf { get; set; }

        public bool IsDictionary { get; set; }

        public bool IsArray { get; set; }
    }

    #endregion

    #region 算法模块

    #region 路径算法

    public static class PathAlgorithm
    {
        public static string GetParentPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";

            if (fullPath.Contains('[') && fullPath.Contains(']'))
            {
                var lastBracketIndex = fullPath.LastIndexOf('[');
                if (lastBracketIndex > 0)
                {
                    return fullPath.Substring(0, lastBracketIndex);
                }
            }

            var lastDotIndex = fullPath.LastIndexOf('.');
            return lastDotIndex > 0 ? fullPath.Substring(0, lastDotIndex) : "";
        }

        public static string BuildNodePath(string parentPath, string key)
        {
            if (string.IsNullOrEmpty(parentPath)) return key;

            if (key.StartsWith("[")) return $"{parentPath}{key}";

            return $"{parentPath}.{key}";
        }

        public static void AddToDictionaryByPath(
            Dictionary<string, object> dict,
            string fullPath,
            object value)
        {
            if (string.IsNullOrEmpty(fullPath)) return;

            var segments = fullPath.Split('.');
            var current = dict;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                if (!current.TryGetValue(segment, out var next) || next is not Dictionary<string, object>)
                {
                    next = new Dictionary<string, object>();
                    current[segment] = next;
                }
                current = (Dictionary<string, object>)next;
            }

            current[segments.Last()] = value;
        }

        public static HashSet<string> CollectLeafPaths(
            Dictionary<string, object> dict,
            string currentPath = "")
        {
            var paths = new HashSet<string>();
            CollectLeafPathsRecursive(dict, currentPath, paths);
            return paths;
        }

        private static void CollectLeafPathsRecursive(
            Dictionary<string, object> dict,
            string currentPath,
            HashSet<string> paths)
        {
            foreach (var kvp in dict)
            {
                var path = string.IsNullOrEmpty(currentPath) ? kvp.Key : $"{currentPath}.{kvp.Key}";
                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    CollectLeafPathsRecursive(nestedDict, path, paths);
                }
                else if (kvp.Value is List<object> array)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        var arrayPath = $"{path}[{i}]";
                        if (array[i] is Dictionary<string, object> arrayDict)
                        {
                            CollectLeafPathsRecursive(arrayDict, arrayPath, paths);
                        }
                        else
                        {
                            paths.Add(arrayPath);
                        }
                    }
                }
                else
                {
                    paths.Add(path);
                }
            }
        }
    }

    #endregion

    #region 遍历算法

    public static class TraversalAlgorithm
    {
        public static void TraverseStructure(
            object structure,
            string parentPath,
            int currentDepth,
            DictionaryTraverser.TraversalResult result,
            TraversalContext context)
        {
            if (structure == null) return;

            switch (structure)
            {
                case Dictionary<string, object> dict:
                    TraverseDictionary(dict, parentPath, currentDepth, result, context);
                    break;
                case List<object> array when context.HandleArrays:
                    TraverseArray(array, parentPath, currentDepth, result, context);
                    break;
                default:
                    break;
            }
        }

        private static void TraverseDictionary(
            Dictionary<string, object> dictionary,
            string parentPath,
            int currentDepth,
            DictionaryTraverser.TraversalResult result,
            TraversalContext context)
        {
            if (dictionary == null) return;

            foreach (var kvp in dictionary)
            {
                if (context.KeysToSkip != null && context.KeysToSkip.Contains(kvp.Key)) continue;

                string nodePath = PathAlgorithm.BuildNodePath(parentPath, kvp.Key);
                int nodeDepth = currentDepth + 1;

                if (ShouldAddNode(nodeDepth, context))
                {
                    AddNode(kvp.Key, nodePath, kvp.Value, nodeDepth, result, context);
                }

                if (ShouldRecurse(nodeDepth, context) && kvp.Value != null)
                {
                    TraverseStructure(kvp.Value, nodePath, nodeDepth, result, context);
                }
            }
        }

        private static void TraverseArray(
            List<object> array,
            string parentPath,
            int currentDepth,
            DictionaryTraverser.TraversalResult result,
            TraversalContext context)
        {
            if (array == null) return;

            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i];
                string arrayKey = $"[{i}]";
                string nodePath = PathAlgorithm.BuildNodePath(parentPath, arrayKey);
                int nodeDepth = currentDepth + 1;

                if (ShouldAddNode(nodeDepth, context))
                {
                    AddNode(arrayKey, nodePath, item, nodeDepth, result, context);
                }

                if (item != null && ShouldRecurse(nodeDepth, context))
                {
                    TraverseStructure(item, nodePath, nodeDepth, result, context);
                }
            }
        }

        private static bool ShouldAddNode(int nodeDepth, TraversalContext context)
        {
            if (context.TargetDepth == -1)
            {
                return true;
            }

            if (context.StopAtTargetDepth)
            {
                return nodeDepth <= context.TargetDepth;
            }
            else
            {
                return true;
            }
        }

        private static bool ShouldRecurse(int nodeDepth, TraversalContext context)
        {
            if (context.TargetDepth == -1)
            {
                return true;
            }

            if (context.StopAtTargetDepth)
            {
                return nodeDepth < context.TargetDepth;
            }
            else
            {
                return nodeDepth < context.TargetDepth || context.TargetDepth == -1;
            }
        }

        private static void AddNode(
            string key,
            string fullPath,
            object value,
            int depth,
            DictionaryTraverser.TraversalResult result,
            TraversalContext context)
        {
            var node = new NodeInfoBase(key, fullPath, value, depth);
            result.Nodes.Add(node);

            if (context.PreservePaths && !result.KeyPaths.ContainsKey(key))
            {
                result.KeyPaths[key] = fullPath;
            }

            UpdateStatistics(result, depth, context);
        }

        private static void UpdateStatistics(
            DictionaryTraverser.TraversalResult result,
            int depth,
            TraversalContext context)
        {
            if (!context.CollectStatistics) return;

            if (!result.DepthStatistics.ContainsKey(depth))
                result.DepthStatistics[depth] = 0;
            result.DepthStatistics[depth]++;
        }
    }

    #endregion

    #region �ֵ乤���㷨

    public static class DictionaryUtility
    {
        public static int GetMaxDepth(Dictionary<string, object> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0) return 0;

            int maxDepth = 0;
            foreach (var value in dictionary.Values)
            {
                int currentDepth = 0;
                if (value is Dictionary<string, object> nestedDict)
                {
                    currentDepth = GetMaxDepth(nestedDict);
                }
                else if (value is List<object> array)
                {
                    currentDepth = GetArrayMaxDepth(array);
                }
                maxDepth = Math.Max(maxDepth, currentDepth);
            }

            return maxDepth + 1;
        }

        private static int GetArrayMaxDepth(List<object> array)
        {
            if (array == null || array.Count == 0) return 0;

            int maxDepth = 0;
            foreach (var item in array)
            {
                int currentDepth = 0;
                if (item is Dictionary<string, object> nestedDict)
                {
                    currentDepth = GetMaxDepth(nestedDict);
                }
                else if (item is List<object> nestedArray)
                {
                    currentDepth = GetArrayMaxDepth(nestedArray);
                }
                maxDepth = Math.Max(maxDepth, currentDepth);
            }

            return maxDepth + 1;
        }

        public static int CountLeafNodes(Dictionary<string, object> dict)
        {
            if (dict == null) return 0;

            int count = 0;
            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    count += CountLeafNodes(nestedDict);
                }
                else if (kvp.Value is List<object> array)
                {
                    count += array.Count;
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        public static Dictionary<string, object> FlattenDictionary(Dictionary<string, object> dict)
        {
            var flatDict = new Dictionary<string, object>();
            FlattenDictionaryRecursive(dict, "", flatDict);
            return flatDict;
        }

        private static void FlattenDictionaryRecursive(
            Dictionary<string, object> dict,
            string prefix,
            Dictionary<string, object> result)
        {
            foreach (var kvp in dict)
            {
                var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    FlattenDictionaryRecursive(nestedDict, key, result);
                }
                else
                {
                    result[key] = kvp.Value;
                }
            }
        }

        public static void MergeDictionaries(
            Dictionary<string, object> target,
            Dictionary<string, object> source)
        {
            foreach (var kvp in source)
            {
                if (kvp.Value is Dictionary<string, object> sourceDict)
                {
                    if (!target.TryGetValue(kvp.Key, out var existing) || existing is not Dictionary<string, object>)
                    {
                        target[kvp.Key] = new Dictionary<string, object>();
                    }
                    MergeDictionaries((Dictionary<string, object>)target[kvp.Key], sourceDict);
                }
                else if (!target.ContainsKey(kvp.Key))
                {
                    target[kvp.Key] = kvp.Value;
                }
            }
        }

        public static List<string> GetSampleKeys(Dictionary<string, object> dict, int maxCount)
        {
            var samples = new List<string>();
            CollectSampleKeys(dict, samples, maxCount);
            return samples;
        }

        private static void CollectSampleKeys(
            Dictionary<string, object> dict,
            List<string> samples,
            int maxCount)
        {
            foreach (var kvp in dict)
            {
                if (samples.Count >= maxCount) return;

                if (kvp.Value is Dictionary<string, object>)
                {
                    samples.Add($"{kvp.Key}(...)");
                }
                else
                {
                    samples.Add(kvp.Key);
                }
            }
        }
    }

    #endregion

    #region �������㷨

    public static class KeySearchAlgorithm
    {
        public static object GetStartObject(
            Dictionary<string, object> dictionary,
            TraversalContext context)
        {
            if (!string.IsNullOrEmpty(context.StartKey))
            {
                return FindKey(dictionary, context.StartKey, context.KeysToSkip);
            }
            return dictionary;
        }

        public static object FindKey(
            Dictionary<string, object> dictionary,
            string targetKey,
            HashSet<string> keysToSkip)
        {
            if (dictionary == null) return null;

            if (dictionary.TryGetValue(targetKey, out var value) &&
                (keysToSkip == null || !keysToSkip.Contains(targetKey)))
            {
                return value;
            }

            foreach (var kvp in dictionary)
            {
                if (keysToSkip != null && keysToSkip.Contains(kvp.Key)) continue;

                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    var found = FindKey(nestedDict, targetKey, keysToSkip);
                    if (found != null) return found;
                }
                else if (kvp.Value is List<object> array)
                {
                    foreach (var item in array)
                    {
                        if (item is Dictionary<string, object> arrayDict)
                        {
                            var found = FindKey(arrayDict, targetKey, keysToSkip);
                            if (found != null) return found;
                        }
                    }
                }
            }

            return null;
        }
    }

    #endregion

    #region �����֤��?

    public static class DepthValidator
    {
        public static ValidationResult ValidateDepth(
            DictionaryTraverser.TraversalResult result,
            TraversalContext context)
        {
            var validation = new ValidationResult();

            if (result == null || result.Nodes.Count == 0)
            {
                validation.IsValid = false;
                validation.Message = "�������Ϊ�գ��޽ڵ㣩��У��ʧ��";
                return validation;
            }

            var actualDepthDistribution = result.Nodes
                .GroupBy(n => n.Depth)
                .ToDictionary(g => g.Key, g => g.Count());

            validation.ActualDepths = actualDepthDistribution.Keys.OrderBy(d => d).ToList();
            validation.MaxActualDepth = result.MaxDepth;
            validation.TotalNodes = result.Nodes.Count;

            if (context.TargetDepth == -1)
            {
                validation.IsValid = true;
                validation.Message = "��������ƣ��������";
            }
            else if (context.StopAtTargetDepth)
            {
                var unexpectedDepths = actualDepthDistribution.Keys
                    .Where(d => d > context.TargetDepth)
                    .ToList();

                if (unexpectedDepths.Any())
                {
                    validation.IsValid = false;
                    validation.Message = $"���ֳ���Ŀ�����{context.TargetDepth}�Ľڵ㣺���?{string.Join(", ", unexpectedDepths)}";
                    validation.UnexpectedDepths = unexpectedDepths;
                }
                else
                {
                    validation.IsValid = true;
                    validation.Message = $"��ȿ�����ȷ��������Ϊ{validation.MaxActualDepth}";
                }
            }
            else
            {
                validation.IsValid = true;
                validation.Message = $"������ɣ�������Ϊ{validation.MaxActualDepth}";
            }

            return validation;
        }

        public static int CalibrateTargetDepth(
            Dictionary<string, object> dictionary,
            int desiredDepth,
            bool isOneBased = true)
        {
            if (dictionary == null) return desiredDepth;

            var maxDepth = DictionaryUtility.GetMaxDepth(dictionary);
            int actualMaxDepth = maxDepth - (isOneBased ? 1 : 0);

            if (desiredDepth == -1) return -1;

            int calibratedDepth = isOneBased ? desiredDepth - 1 : desiredDepth;

            if (calibratedDepth > actualMaxDepth)
            {
                calibratedDepth = actualMaxDepth;
            }

            return isOneBased ? calibratedDepth + 1 : calibratedDepth;
        }

        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; }
            public List<int> ActualDepths { get; set; } = new();
            public int MaxActualDepth { get; set; }
            public int TotalNodes { get; set; }
            public List<int> UnexpectedDepths { get; set; } = new();
        }
    }

    #endregion

    #endregion

    #region �ֵ������?

    public static class DictionaryTraverser
    {
        public class TraversalResult
        {
            public List<NodeInfoBase> Nodes { get; set; } = new();
            public Dictionary<string, string> KeyPaths { get; set; } = new();
            public Dictionary<int, int> DepthStatistics { get; set; } = new();
            public int MaxDepth { get; set; }

            public List<NodeInfoBase> GetNodesAtDepth(int depth) =>
                Nodes.Where(n => n.Depth == depth).ToList();

            public List<NodeInfoBase> GetLeafNodes() =>
                Nodes.Where(n => n.IsLeaf).ToList();

            public List<KeyValuePair<string, object>> GetLeafKeyValuePairs() =>
                Nodes.Where(n => n.IsLeaf)
                     .Select(n => new KeyValuePair<string, object>(n.FullPath, n.Value))
                     .ToList();
        }

        public static List<object> TraverseFromKey(
            Dictionary<string, object> dictionary,
            string startKey = null,
            int targetLevel = -1,
            HashSet<string> keysToSkip = null)
        {
            if (dictionary == null)
                return new List<object>();

            keysToSkip ??= new HashSet<string>();

            int targetDepth = targetLevel == -1 ? -1 : targetLevel - 1;


            var context = new TraversalContext
            {
                StartKey = startKey,
                TargetDepth = targetDepth,
                KeysToSkip = keysToSkip,
                CollectStatistics = false,
                PreservePaths = false,
                StopAtTargetDepth = true,
                DepthMode = DepthProcessingMode.Inclusive
            };

     
            if (!context.Validate())
            {
                throw new InvalidOperationException("����������������Ч");
            }

      
            var result = EnhancedTraverse(dictionary, context);

        
            if (targetLevel == -1)
            {
                return result.GetLeafNodes()
                    .Where(n => n.Value != null)
                    .Select(n => n.Value)
                    .ToList();
            }
            else
            {
    
                var nodesAtTargetDepth = result.GetNodesAtDepth(targetDepth)
                    .Where(n => n.Depth == targetDepth)
                    .ToList();

  
                return nodesAtTargetDepth
                    .Select(n => (object)n.Key)
                    .ToList();
            }
        }


        public static TraversalResult EnhancedTraverse(
            Dictionary<string, object> dictionary,
            TraversalContext context = null)
        {
            context ??= new TraversalContext();
            var result = new TraversalResult();

            if (dictionary == null) return result;

            var startObject = KeySearchAlgorithm.GetStartObject(dictionary, context);
            if (startObject == null) return result;

            TraversalAlgorithm.TraverseStructure(startObject, "", 0, result, context);
            result.MaxDepth = result.DepthStatistics.Keys.DefaultIfEmpty(0).Max();
            return result;
        }


        public static List<KeyValuePair<string, object>> GetLevelKeyValuePairs(
            Dictionary<string, object> dictionary,
            int depth,
            bool includeChildren = false,
            HashSet<string> keysToSkip = null)
        {
            if (dictionary == null)
                return new List<KeyValuePair<string, object>>();

            var context = new TraversalContext
            {
                TargetDepth = depth,
                KeysToSkip = keysToSkip ?? new HashSet<string>(),
                CollectStatistics = false,
                StopAtTargetDepth = !includeChildren,
                DepthMode = includeChildren ? DepthProcessingMode.Inclusive : DepthProcessingMode.Exact
            };

            var result = EnhancedTraverse(dictionary, context);

            var filteredNodes = includeChildren
                ? result.Nodes.Where(n => n.Depth <= depth)
                : result.Nodes.Where(n => n.Depth == depth);

            return filteredNodes
                .Where(n => n.Value != null && !(n.Value is Dictionary<string, object>) && !(n.Value is List<object>))
                .Select(n => new KeyValuePair<string, object>(n.FullPath, n.Value))
                .ToList();
        }


        public static List<string> FindAllKeys(
            Dictionary<string, object> dictionary,
            HashSet<string> keysToSkip = null)
        {
            if (dictionary == null)
                return new List<string>();

            keysToSkip ??= new HashSet<string>();

            var context = new TraversalContext
            {
                CollectStatistics = false,
                KeysToSkip = keysToSkip,
                TargetDepth = -1
            };

            var result = EnhancedTraverse(dictionary, context);

            return result.Nodes
                .Where(n => !string.IsNullOrEmpty(n.Name))
                .Select(n => n.Name)
                .Distinct()
                .ToList();
        }

        public static Dictionary<int, List<object>> TraverseAllLevels(
            Dictionary<string, object> dictionary,
            HashSet<string> keysToSkip = null)
        {
            var allLevelsResult = new Dictionary<int, List<object>>();

            if (dictionary == null)
                return allLevelsResult;

            var contextForMaxDepth = new TraversalContext
            {
                CollectStatistics = true,
                KeysToSkip = keysToSkip ?? new HashSet<string>(),
                TargetDepth = -1
            };

            var traversalResult = EnhancedTraverse(dictionary, contextForMaxDepth);
            int maxDepth = traversalResult.MaxDepth;

            for (int level = 1; level <= maxDepth + 1; level++)
            {
                var levelContext = new TraversalContext
                {
                    CollectStatistics = false,
                    KeysToSkip = keysToSkip ?? new HashSet<string>(),
                    TargetDepth = level - 1,
                    StopAtTargetDepth = true
                };

                var levelResult = EnhancedTraverse(dictionary, levelContext);

                var keys = levelResult.GetNodesAtDepth(level - 1)
                    .Select(n => (object)n.Name)
                    .ToList();

                allLevelsResult[level] = keys;
            }

            return allLevelsResult;
        }

        private static int GetMaxDepth(Dictionary<string, object> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0)
                return 0;

            var context = new TraversalContext
            {
                CollectStatistics = true,
                TargetDepth = -1
            };

            var result = EnhancedTraverse(dictionary, context);
            return result.MaxDepth + 1;
        }
    }

    #endregion

    #region �ֵ������?

    public static class DictionaryClassifier
    {
        #region ���úͽ����?

        public class ClassificationConfig
        {
            public ClassificationMode Mode { get; set; } = ClassificationMode.Auto;

            public int TargetDepth { get; set; } = 2;

            public int MinGroupCount { get; set; } = 1;

            public int? MaxGroupCount { get; set; }

            public bool ConsiderArrayNesting { get; set; } = true;

            public bool PreserveKeyStructure { get; set; } = true;

            public HashSet<string> KeysToSkip { get; set; } = new();

            public WeightConfig Weights { get; set; } = new();

            public bool DistributeGlobalInfo { get; set; } = true;

            public int GlobalInfoMaxDepth { get; set; } = 2;
        }

        public class WeightConfig
        {
            public float DepthWeight { get; set; } = 1.2f;
            public float LeafDensityWeight { get; set; } = 0.8f;
            public float ArrayWeight { get; set; } = 0.5f;
            public float BreadthWeight { get; set; } = 0.7f;
            public float ChildCountWeight { get; set; } = 1.0f;
            public float ValueNodeWeight { get; set; } = 0.8f;
        }

        public enum ClassificationMode
        {
            Auto,

            DepthFirst,

            BreadthFirst,

            FixedDepth
        }

        public class ClassificationResult
        {
            public List<Dictionary<string, object>> Groups { get; set; } = new();
            public List<int> Scores { get; set; } = new();
            public Dictionary<string, object> Statistics { get; set; } = new();
            public List<string> GroupDescriptions { get; set; } = new();

            public int TotalGroups => Groups.Count;
            public int TotalKeys => Groups.Sum(g => CountLeafNodes(g));
        }

        #endregion

        #region ���ķ��෽��

        public static ClassificationResult ClassifyDictionary(
            Dictionary<string, object> dictionary,
            ClassificationConfig config = null)
        {
            config ??= new ClassificationConfig();

            if (dictionary == null || dictionary.Count == 0)
            {
                return new ClassificationResult
                {
                    Statistics = { ["Message"] = "Empty dictionary" }
                };
            }

            try
            {
                var analysis = AnalyzeDictionaryStructure(dictionary, config);

                ClassificationResult result = config.Mode switch
                {
                    ClassificationMode.DepthFirst => DepthBasedClassification(dictionary, analysis, config),
                    ClassificationMode.BreadthFirst => BreadthBasedClassification(dictionary, analysis, config),
                    ClassificationMode.FixedDepth => FixedDepthClassification(dictionary, config),
                    _ => AutoClassification(dictionary, analysis, config)
                };

                result = PostProcessGroups(result, config);

                result.GroupDescriptions = GenerateGroupDescriptions(result.Groups);

                return result;
            }
            catch (Exception ex)
            {
                return new ClassificationResult
                {
                    Groups = { dictionary },
                    Scores = { dictionary.Count },
                    Statistics =
                {
                    ["Error"] = ex.Message,
                    ["Fallback"] = true,
                    ["Message"] = "Using fallback strategy"
                }
                };
            }
        }

        #endregion

        #region ˽�к����߼�

        public class StructureAnalysis
        {
            public int MaxDepth { get; set; }
            public int TotalNodes { get; set; }
            public int TotalLeafNodes { get; set; }
            public int MaxArrayDepth { get; set; }
            public double AverageBreadth { get; set; }
            public Dictionary<int, int> DepthDistribution { get; set; } = new();
            public Dictionary<int, int> LeafDepthDistribution { get; set; } = new();
            public List<Dictionary<string, object>> GlobalNodes { get; set; } = new();
            public bool IsBalanced { get; set; }
            public double ComplexityScore { get; set; }
        }

        public static StructureAnalysis AnalyzeDictionaryStructure(
            Dictionary<string, object> dictionary,
            ClassificationConfig config)
        {
            var analysis = new StructureAnalysis();

            var traversalResult = EnhancedTraverse(dictionary, config.KeysToSkip, config.ConsiderArrayNesting);

            analysis.TotalNodes = traversalResult.Nodes.Count;
            analysis.MaxDepth = traversalResult.MaxDepth;
            analysis.DepthDistribution = traversalResult.DepthStatistics;

            CalculateLeafStatistics(traversalResult.Nodes, analysis);

            analysis.MaxArrayDepth = config.ConsiderArrayNesting
                ? CalculateMaxArrayDepth(dictionary)
                : 0;

            analysis.AverageBreadth = CalculateAverageBreadth(analysis.DepthDistribution);

            analysis.IsBalanced = IsBalancedStructure(analysis.LeafDepthDistribution, analysis.MaxDepth);

            analysis.ComplexityScore = CalculateComplexityScore(analysis, config.Weights);

            if (config.DistributeGlobalInfo)
            {
                analysis.GlobalNodes = CollectGlobalNodes(dictionary, config.GlobalInfoMaxDepth);
            }

            return analysis;
        }

        public static ClassificationResult DepthBasedClassification(
            Dictionary<string, object> dictionary,
            StructureAnalysis analysis,
            ClassificationConfig config)
        {
            int optimalDepth = CalculateOptimalDepth(analysis, config.Weights);
            optimalDepth = Math.Clamp(optimalDepth, 1, analysis.MaxDepth);

            var targetNodes = CollectNodesAtDepth(dictionary, optimalDepth, config.KeysToSkip);

            if (targetNodes.Count == 0)
            {
                optimalDepth = FindNearestDepthWithNodes(dictionary, optimalDepth, config.KeysToSkip);
                targetNodes = CollectNodesAtDepth(dictionary, optimalDepth, config.KeysToSkip);
            }

            var groups = GroupNodesByParent(targetNodes, config.PreserveKeyStructure);

            var selectedGroups = ScoreAndSelectGroups(groups, optimalDepth, config);

            return BuildClassificationResult(selectedGroups, analysis, "DepthFirst");
        }

        public static ClassificationResult BreadthBasedClassification(
            Dictionary<string, object> dictionary,
            StructureAnalysis analysis,
            ClassificationConfig config)
        {
            var baseGroups = DepthBasedClassification(dictionary, analysis, config).Groups;

            var mergedGroups = MergeSmallGroups(baseGroups, config.MinGroupCount);

            if (config.DistributeGlobalInfo && analysis.GlobalNodes.Count > 0)
            {
                foreach (var group in mergedGroups)
                {
                    AddGlobalInfoToGroup(group, analysis.GlobalNodes);
                }
            }

            return BuildClassificationResult(
                mergedGroups.Select(g => (g, ScoreGroup(g, 0, config.Weights))).ToList(),
                analysis,
                "BreadthFirst");
        }

        public static ClassificationResult FixedDepthClassification(
            Dictionary<string, object> dictionary,
            ClassificationConfig config)
        {
            var targetDepth = config.TargetDepth;
            var targetNodes = CollectNodesAtDepth(dictionary, targetDepth, config.KeysToSkip);

            if (targetNodes.Count == 0)
            {
                targetDepth = FindNearestDepthWithNodes(dictionary, targetDepth, config.KeysToSkip);
                targetNodes = CollectNodesAtDepth(dictionary, targetDepth, config.KeysToSkip);
            }

            var groups = GroupNodesByParent(targetNodes, config.PreserveKeyStructure);
            var selectedGroups = ScoreAndSelectGroups(groups, targetDepth, config);

            return BuildClassificationResult(selectedGroups, null, "FixedDepth");
        }

        public static ClassificationResult AutoClassification(
            Dictionary<string, object> dictionary,
            StructureAnalysis analysis,
            ClassificationConfig config)
        {
            if (analysis.ComplexityScore < 10)
            {
                config.TargetDepth = Math.Min(2, analysis.MaxDepth);
                return FixedDepthClassification(dictionary, config);
            }
            else if (analysis.MaxDepth > analysis.AverageBreadth)
            {
                return DepthBasedClassification(dictionary, analysis, config);
            }
            else
            {
                return BreadthBasedClassification(dictionary, analysis, config);
            }
        }

        public static int CalculateOptimalDepth(StructureAnalysis analysis, WeightConfig weights)
        {
            if (analysis.MaxDepth <= 1) return 1;

            var depthScores = new Dictionary<int, double>();
            int bestDepth = 1;
            double bestScore = 0;

            for (int depth = 1; depth <= analysis.MaxDepth; depth++)
            {
                double score = depth * weights.DepthWeight;

                if (analysis.LeafDepthDistribution.TryGetValue(depth, out int leafCount))
                {
                    score += leafCount * weights.LeafDensityWeight;
                }

                if (analysis.DepthDistribution.TryGetValue(depth, out int nodeCount))
                {
                    score += nodeCount * weights.BreadthWeight * 0.5;
                }

                depthScores[depth] = score;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDepth = depth;
                }
            }

            return bestDepth;
        }

        public static List<NodeInfo> CollectNodesAtDepth(
            Dictionary<string, object> dictionary,
            int targetDepth,
            HashSet<string> keysToSkip)
        {
            var result = new List<NodeInfo>();

            var traversalResult = EnhancedTraverse(dictionary, keysToSkip, true, targetDepth);

            result = traversalResult.Nodes
                .Where(n => n.Depth == targetDepth)
                .Select(n => new NodeInfo
                {
                    Key = n.Key,
                    FullPath = n.FullPath,
                    Value = n.Value,
                    Depth = n.Depth,
                    ParentPath = n.ParentPath,
                    IsLeaf = n.IsLeaf,
                    IsDictionary = n.IsDictionary,
                    IsArray = n.IsArray
                })
                .ToList();

            return result;
        }

        public static List<(List<NodeInfo> Nodes, string ParentPath)> GroupNodesByParent(
            List<NodeInfo> nodes,
            bool preserveStructure)
        {
            return nodes
                .GroupBy(n => preserveStructure ? n.ParentPath : "")
                .Select(g => (g.ToList(), g.Key))
                .ToList();
        }

        public static List<(Dictionary<string, object> Dict, int Score)> ScoreAndSelectGroups(
            List<(List<NodeInfo> Nodes, string ParentPath)> groups,
            int targetDepth,
            ClassificationConfig config)
        {
            var scoredGroups = groups
                .Select(g => (
                    Dict: CreateDictionaryFromNodes(g.Nodes, config.PreserveKeyStructure),
                    Score: CalculateGroupScore(g.Nodes, targetDepth, config.Weights)
                ))
                .Where(x => x.Dict.Count > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (config.MaxGroupCount.HasValue && scoredGroups.Count > config.MaxGroupCount.Value)
            {
                scoredGroups = scoredGroups.Take(config.MaxGroupCount.Value).ToList();
            }

            while (scoredGroups.Count < config.MinGroupCount && groups.Count > scoredGroups.Count)
            {
                var remainingGroups = groups
                    .Skip(scoredGroups.Count)
                    .FirstOrDefault();

                if (remainingGroups.Nodes == null) break;

                var newGroup = (
                    Dict: CreateDictionaryFromNodes(remainingGroups.Nodes, config.PreserveKeyStructure),
                    Score: CalculateGroupScore(remainingGroups.Nodes, targetDepth, config.Weights)
                );

                if (newGroup.Dict.Count > 0)
                {
                    scoredGroups.Add(newGroup);
                }
            }

            return scoredGroups;
        }

        private static int CalculateGroupScore(List<NodeInfo> nodes, int targetDepth, WeightConfig weights)
        {
            if (nodes.Count == 0) return 0;

            float score = nodes.Count * weights.ChildCountWeight;
            score *= (1 + targetDepth * weights.DepthWeight);

            int arrayNodeCount = nodes.Count(n => n.IsArray);
            score += arrayNodeCount * weights.ArrayWeight;

            int valueNodeCount = nodes.Count(n => n.IsLeaf);
            score += valueNodeCount * weights.ValueNodeWeight;

            return Math.Max(1, (int)Math.Round(score));
        }

        private static Dictionary<string, object> CreateDictionaryFromNodes(
            List<NodeInfo> nodes,
            bool preserveStructure)
        {
            var dict = new Dictionary<string, object>();

            foreach (var node in nodes)
            {
                string key = preserveStructure ? node.Key : node.FullPath;
                if (!dict.ContainsKey(key))
                {
                    dict[key] = node.Value;
                }
            }

            return dict;
        }

        private static List<Dictionary<string, object>> MergeSmallGroups(
            List<Dictionary<string, object>> groups,
            int minGroupCount)
        {
            if (groups.Count >= minGroupCount) return groups;

            var mergedDict = new Dictionary<string, object>();
            foreach (var group in groups)
            {
                foreach (var kvp in group)
                {
                    if (!mergedDict.ContainsKey(kvp.Key))
                    {
                        mergedDict[kvp.Key] = kvp.Value;
                    }
                }
            }

            return new List<Dictionary<string, object>> { mergedDict };
        }

        private static ClassificationResult PostProcessGroups(
            ClassificationResult result,
            ClassificationConfig config)
        {
            if (result.Groups.Count <= 1) return result;

            result.Groups = result.Groups.Where(g => g.Count > 0).ToList();

            if (result.Scores.Count > result.Groups.Count)
            {
                result.Scores = result.Scores.Take(result.Groups.Count).ToList();
            }
            else if (result.Scores.Count < result.Groups.Count)
            {
                while (result.Scores.Count < result.Groups.Count)
                {
                    result.Scores.Add(1);
                }
            }

            return result;
        }

        #endregion

        #region ��������

        public class TraversalResult
        {
            public List<NodeInfo> Nodes { get; set; } = new();
            public int MaxDepth { get; set; }
            public Dictionary<int, int> DepthStatistics { get; set; } = new();
        }

        public static TraversalResult EnhancedTraverse(
            Dictionary<string, object> dictionary,
            HashSet<string> keysToSkip,
            bool handleArrays,
            int? targetDepth = null)
        {
            var result = new TraversalResult();

            var context = new TraversalContext
            {
                KeysToSkip = keysToSkip ?? new HashSet<string>(),
                HandleArrays = handleArrays,
                TargetDepth = targetDepth ?? -1,
                StopAtTargetDepth = targetDepth.HasValue,
                CollectStatistics = true
            };

            var coreResult = DictionaryTraverser.EnhancedTraverse(dictionary, context);

            result.Nodes = coreResult.Nodes.Select(n => new NodeInfo
            {
                Key = n.Key,
                FullPath = n.FullPath,
                Value = n.Value,
                Depth = n.Depth,
                ParentPath = n.ParentPath,
                IsLeaf = n.IsLeaf,
                IsDictionary = n.IsDictionary,
                IsArray = n.IsArray
            }).ToList();

            result.MaxDepth = coreResult.MaxDepth;
            result.DepthStatistics = coreResult.DepthStatistics;

            return result;
        }

        private static void CalculateLeafStatistics(List<NodeInfo> nodes, StructureAnalysis analysis)
        {
            var leafDepthStats = new Dictionary<int, int>();
            int totalLeafNodes = 0;

            foreach (var node in nodes)
            {
                if (node.IsLeaf)
                {
                    if (!leafDepthStats.ContainsKey(node.Depth))
                        leafDepthStats[node.Depth] = 0;
                    leafDepthStats[node.Depth]++;
                    totalLeafNodes++;
                }
            }

            analysis.LeafDepthDistribution = leafDepthStats;
            analysis.TotalLeafNodes = totalLeafNodes;
        }

        private static int CalculateMaxArrayDepth(Dictionary<string, object> dictionary)
        {
            int maxDepth = 0;

            void AnalyzeObject(object obj, int currentDepth)
            {
                if (obj == null) return;

                if (obj is List<object> array)
                {
                    maxDepth = Math.Max(maxDepth, currentDepth + 1);
                    foreach (var item in array)
                    {
                        AnalyzeObject(item, currentDepth + 1);
                    }
                }
                else if (obj is Dictionary<string, object> dict)
                {
                    foreach (var value in dict.Values)
                    {
                        AnalyzeObject(value, currentDepth);
                    }
                }
            }

            AnalyzeObject(dictionary, 0);
            return maxDepth;
        }

        private static double CalculateAverageBreadth(Dictionary<int, int> depthDistribution)
        {
            if (depthDistribution == null || depthDistribution.Count == 0)
                return 0;

            double totalNodes = depthDistribution.Values.Sum();
            return totalNodes / depthDistribution.Count;
        }

        private static bool IsBalancedStructure(Dictionary<int, int> leafDepthStats, int maxDepth)
        {
            if (leafDepthStats == null || leafDepthStats.Count <= 2)
                return true;

            var leafDepths = leafDepthStats.Keys.ToList();
            if (leafDepths.Count == 0) return false;

            int maxLeafDepth = leafDepths.Max();
            int minLeafDepth = leafDepths.Min();
            int leafDepthRange = maxLeafDepth - minLeafDepth;

            return leafDepthRange < (maxDepth * 2.0 / 3.0);
        }

        public static double CalculateComplexityScore(StructureAnalysis analysis, WeightConfig weights)
        {
            double score = analysis.MaxDepth * weights.DepthWeight;

            if (analysis.TotalLeafNodes > 0 && analysis.TotalNodes > 0)
            {
                double leafDensity = (double)analysis.TotalLeafNodes / analysis.TotalNodes;
                score += leafDensity * weights.LeafDensityWeight * 10;
            }

            score += analysis.MaxArrayDepth * weights.ArrayWeight;
            score += analysis.AverageBreadth * weights.BreadthWeight * 0.1;

            return score;
        }

        private static List<Dictionary<string, object>> CollectGlobalNodes(
            Dictionary<string, object> dictionary,
            int maxDepth)
        {
            var globalNodes = new List<Dictionary<string, object>>();

            void CollectNodes(Dictionary<string, object> dict, int currentDepth, string currentPath)
            {
                if (currentDepth > maxDepth) return;

                foreach (var kvp in dict)
                {
                    string keyPath = string.IsNullOrEmpty(currentPath)
                        ? kvp.Key
                        : $"{currentPath}.{kvp.Key}";

                    if (!(kvp.Value is Dictionary<string, object>) && !(kvp.Value is List<object>))
                    {
                        var nodeDict = new Dictionary<string, object> { { keyPath, kvp.Value } };
                        globalNodes.Add(nodeDict);
                    }
                    else if (kvp.Value is Dictionary<string, object> nestedDict)
                    {
                        CollectNodes(nestedDict, currentDepth + 1, keyPath);
                    }
                }
            }

            CollectNodes(dictionary, 0, "");
            return globalNodes;
        }

        private static void AddGlobalInfoToGroup(
            Dictionary<string, object> group,
            List<Dictionary<string, object>> globalNodes)
        {
            foreach (var nodeDict in globalNodes)
            {
                foreach (var kvp in nodeDict)
                {
                    if (!group.ContainsKey(kvp.Key))
                    {
                        group[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        private static int FindNearestDepthWithNodes(
            Dictionary<string, object> dictionary,
            int targetDepth,
            HashSet<string> keysToSkip)
        {
            var analysis = EnhancedTraverse(dictionary, keysToSkip, true);
            var depthsWithNodes = analysis.DepthStatistics.Keys.OrderBy(d => d).ToList();

            if (depthsWithNodes.Count == 0) return targetDepth;

            for (int depth = targetDepth - 1; depth >= depthsWithNodes.First(); depth--)
            {
                if (analysis.DepthStatistics.ContainsKey(depth))
                    return depth;
            }

            for (int depth = targetDepth + 1; depth <= depthsWithNodes.Last(); depth++)
            {
                if (analysis.DepthStatistics.ContainsKey(depth))
                    return depth;
            }

            return targetDepth;
        }

        private static ClassificationResult BuildClassificationResult(
            List<(Dictionary<string, object> Dict, int Score)> scoredGroups,
            StructureAnalysis analysis,
            string strategy)
        {
            var result = new ClassificationResult
            {
                Groups = scoredGroups.Select(g => g.Dict).ToList(),
                Scores = scoredGroups.Select(g => g.Score).ToList()
            };

            result.Statistics["Strategy"] = strategy;
            result.Statistics["GroupCount"] = result.TotalGroups;
            result.Statistics["TotalKeys"] = result.TotalKeys;

            if (analysis != null)
            {
                result.Statistics["MaxDepth"] = analysis.MaxDepth;
                result.Statistics["ComplexityScore"] = analysis.ComplexityScore;
                result.Statistics["IsBalanced"] = analysis.IsBalanced;
            }

            return result;
        }

        private static List<string> GenerateGroupDescriptions(List<Dictionary<string, object>> groups)
        {
            return groups.Select((group, i) =>
            {
                var leafCount = CountLeafNodes(group);
                var sampleKeys = GetSampleKeys(group, 3);
                return $"Group {i + 1}: {leafCount} leaf nodes" +
                       (sampleKeys.Count > 0 ? $", Samples: {string.Join(", ", sampleKeys)}" : "");
            }).ToList();
        }

        public static int CountLeafNodes(Dictionary<string, object> dict)
        {
            int count = 0;

            void CountNodes(Dictionary<string, object> currentDict)
            {
                foreach (var kvp in currentDict)
                {
                    if (kvp.Value is Dictionary<string, object> nestedDict)
                    {
                        CountNodes(nestedDict);
                    }
                    else if (kvp.Value is List<object> list)
                    {
                        foreach (var item in list)
                        {
                            if (item is Dictionary<string, object> itemDict)
                            {
                                CountNodes(itemDict);
                            }
                            else
                            {
                                count++;
                            }
                        }
                    }
                    else
                    {
                        count++;
                    }
                }
            }

            CountNodes(dict);
            return count;
        }

        public static List<string> GetSampleKeys(Dictionary<string, object> dict, int maxSamples)
        {
            var samples = new List<string>();

            void CollectKeys(Dictionary<string, object> currentDict, string currentPath)
            {
                foreach (var kvp in currentDict)
                {
                    string keyPath = string.IsNullOrEmpty(currentPath)
                        ? kvp.Key
                        : $"{currentPath}.{kvp.Key}";

                    if (samples.Count < maxSamples)
                    {
                        samples.Add(keyPath);
                    }
                    else
                    {
                        return;
                    }

                    if (kvp.Value is Dictionary<string, object> nestedDict)
                    {
                        CollectKeys(nestedDict, keyPath);
                    }
                }
            }

            CollectKeys(dict, "");
            return samples;
        }

        private static int ScoreGroup(Dictionary<string, object> group, int depth, WeightConfig weights)
        {
            var leafCount = CountLeafNodes(group);
            return Math.Max(1, (int)(leafCount * weights.ChildCountWeight * (1 + depth * weights.DepthWeight)));
        }

        #endregion
    }

    #endregion

    #region Ƕ���ֵ䴦����

    public static class NestedDictionaryProcessor
    {
        public static Dictionary<string, object> DeduplicateKeysAdvanced(
            Dictionary<string, object> dictionary,
            string separator = "_",
            bool preserveOriginalWhenPossible = true)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ProcessNestedDictionary(dictionary, usedKeys, separator, preserveOriginalWhenPossible);
        }

        private static Dictionary<string, object> ProcessNestedDictionary(
            Dictionary<string, object> source,
            HashSet<string> usedKeys,
            string separator,
            bool preserveOriginalWhenPossible)
        {
            var result = new Dictionary<string, object>();

            foreach (var kvp in source)
            {
                string uniqueKey = GenerateUniqueKey(kvp.Key, usedKeys, separator, preserveOriginalWhenPossible);
                object processedValue = ProcessValue(kvp.Value, usedKeys, separator, preserveOriginalWhenPossible);
                result[uniqueKey] = processedValue;
            }

            return result;
        }

        private static object ProcessValue(
            object value,
            HashSet<string> usedKeys,
            string separator,
            bool preserveOriginalWhenPossible)
        {
            if (value == null) return null;

            return value switch
            {
                Dictionary<string, object> dict =>
                    ProcessNestedDictionary(dict, usedKeys, separator, preserveOriginalWhenPossible),

                IDictionary<string, object> genericDict =>
                    ProcessNestedDictionary(
                        genericDict.ToDictionary(k => k.Key, k => k.Value),
                        usedKeys, separator, preserveOriginalWhenPossible),

                IEnumerable<object> collection =>
                    ProcessCollection(collection, usedKeys, separator, preserveOriginalWhenPossible),

                _ => value
            };
        }

        private static List<object> ProcessCollection(
            IEnumerable<object> collection,
            HashSet<string> usedKeys,
            string separator,
            bool preserveOriginalWhenPossible)
        {
            var result = new List<object>();

            foreach (var item in collection)
            {
                result.Add(ProcessValue(item, usedKeys, separator, preserveOriginalWhenPossible));
            }

            return result;
        }

        private static string GenerateUniqueKey(
            string originalKey,
            HashSet<string> usedKeys,
            string separator,
            bool preserveOriginalWhenPossible)
        {
            if (preserveOriginalWhenPossible && !usedKeys.Contains(originalKey))
            {
                usedKeys.Add(originalKey);
                return originalKey;
            }

            int counter = 1;
            string newKey;

            do
            {
                newKey = $"{originalKey}{separator}{counter}";
                counter++;
            } while (usedKeys.Contains(newKey));

            usedKeys.Add(newKey);
            return newKey;
        }

        public static List<string> GetAllKeys(Dictionary<string, object> dictionary)
        {
            var keys = new List<string>();
            CollectKeys(dictionary, keys);
            return keys;
        }

        private static void CollectKeys(Dictionary<string, object> dictionary, List<string> keys)
        {
            foreach (var kvp in dictionary)
            {
                keys.Add(kvp.Key);

                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    CollectKeys(nestedDict, keys);
                }
                else if (kvp.Value is IEnumerable<object> collection)
                {
                    foreach (var item in collection)
                    {
                        if (item is Dictionary<string, object> itemDict)
                        {
                            CollectKeys(itemDict, keys);
                        }
                    }
                }
            }
        }
    }

    #endregion

    #endregion
}



namespace DictionaryAlgorithm
{
    public static class KeyConversionUtility
    {
        public static void ConvertArrayValuesToQuotedString(
            object root,
            HashSet<string> keysToConvert,
            bool recursive = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (keysToConvert == null || keysToConvert.Count == 0) return;

            var targets = new List<(string FullPath, object OldValue)>();
            var visited = new HashSet<object>(new ReferenceEqualityComparer());

            void Traverse(object current, string path, int depth)
            {
                if (current == null) return;

                if (current is Dictionary<string, object> dict)
                {
                    if (!visited.Add(dict)) return;

                    foreach (var kvp in dict)
                    {
                        string currentPath = string.IsNullOrEmpty(path) ? kvp.Key : path + "." + kvp.Key;

                        if (keysToConvert.Contains(kvp.Key))
                        {
                            if (recursive || depth == 0)
                                targets.Add((currentPath, kvp.Value));
                        }

                        Traverse(kvp.Value, currentPath, depth + 1);
                    }
                }
                else if (current is List<object> list)
                {
                    if (!visited.Add(list)) return;

                    for (int i = 0; i < list.Count; i++)
                    {
                        string elementPath = path + "[" + i + "]";
                        Traverse(list[i], elementPath, depth + 1);
                    }
                }
            }

            Traverse(root, "", 0);

            foreach (var (fullPath, oldValue) in targets)
            {
                string newValue = ConvertToQuotedString(oldValue);
                SetValueByPath(root, fullPath, newValue);
            }
        }

        public static void ConvertArrayValuesToQuotedString(
            Dictionary<string, object> dictionary,
            HashSet<string> keysToConvert,
            bool recursive = true)
        {
            ConvertArrayValuesToQuotedString((object)dictionary, keysToConvert, recursive);
        }

        #region ˽�и�������

        private static string ConvertToQuotedString(object value)
        {
            if (value == null)
                return "\"null\"";

            if (value is List<object> list)
            {
                var quotedElements = list.Select(item => ConvertToQuotedString(item));
                return string.Join(",", quotedElements);
            }

            string str = value.ToString();
            str = str.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{str}\"";
        }

        private static void SetValueByPath(object root, string path, object newValue)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("·������Ϊ��");

            var segments = path.Split('.');
            object current = root;

            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                bool isLast = i == segments.Length - 1;

                var (key, index) = ParseSegment(segment);

                if (current is Dictionary<string, object> dict)
                {
                    if (string.IsNullOrEmpty(key))
                        throw new InvalidOperationException($"·�� '{path}' �� '{segment}' ���ֵ�����������Ч������Ϊ�ա�");

                    if (!dict.ContainsKey(key))
                        throw new KeyNotFoundException($"·�� '{path}' ��δ�ҵ��� '{key}'��");

                    if (isLast)
                    {
                        if (index.HasValue)
                        {
                            var list = dict[key] as List<object>
                                ?? throw new InvalidOperationException($"·�� '{path}' �м� '{key}' ��ֵ�����б���");
                            if (index.Value < 0 || index.Value >= list.Count)
                                throw new IndexOutOfRangeException($"���� {index.Value} �����б���Χ��");
                            list[index.Value] = newValue;
                        }
                        else
                        {
                            dict[key] = newValue;
                        }
                    }
                    else
                    {
                        object next = index.HasValue
                            ? (dict[key] as List<object>)?[index.Value]
                            : dict[key];
                        current = next ?? throw new InvalidOperationException($"·�� '{path}' ���� null ֵ��");
                    }
                }
                else if (current is List<object> list)
                {
                    if (!string.IsNullOrEmpty(key) || !index.HasValue)
                        throw new InvalidOperationException($"·�� '{path}' �� '{segment}' ���б�����������Ч��ӦΪ \"[n]\"��");

                    if (index.Value < 0 || index.Value >= list.Count)
                        throw new IndexOutOfRangeException($"���� {index.Value} �����б���Χ��");

                    if (isLast)
                    {
                        list[index.Value] = newValue;
                    }
                    else
                    {
                        current = list[index.Value] ?? throw new InvalidOperationException($"·�� '{path}' ���� null ֵ��");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"·�� '{path}' ��ǰ�μȷ��������������");
                }
            }
        }

        private static (string Key, int? Index) ParseSegment(string segment)
        {
            int bracketStart = segment.IndexOf('[');
            if (bracketStart >= 0)
            {
                string key = segment.Substring(0, bracketStart);
                int bracketEnd = segment.IndexOf(']', bracketStart);
                if (bracketEnd < 0)
                    throw new FormatException($"�� '{segment}' ȱ���������š�");
                string indexStr = segment.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                if (!int.TryParse(indexStr, out int index))
                    throw new FormatException($"���� '{indexStr}' ������Ч������");
                return (key, index);
            }
            return (segment, null);
        }

        private class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        #endregion
    }

    public static class JsonKeyConverter
    {
        public static string ConvertMatchingKeysToQuotedString(
            string json,
            HashSet<string> keysToConvert,
            bool recursive = true)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            try
            {
                var token = JToken.Parse(json);
                var root = ConvertJTokenToObject(token);

                KeyConversionUtility.ConvertArrayValuesToQuotedString(root, keysToConvert, recursive);

                return JsonConvert.SerializeObject(root, Formatting.Indented);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"JSON ת��ʧ��: {ex.Message}", ex);
            }
        }

        public static string ConvertArrayValuesToQuotedString(
            string json,
            HashSet<string> keysToConvert,
            bool recursive = true)
        {
            return ConvertMatchingKeysToQuotedString(json, keysToConvert, recursive);
        }

        #region ����ת������

        private static object ConvertJTokenToObject(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in ((JObject)token).Properties())
                    {
                        dict[property.Name] = ConvertJTokenToObject(property.Value);
                    }
                    return dict;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                    {
                        list.Add(ConvertJTokenToObject(item));
                    }
                    return list;

                case JTokenType.Integer:
                    return token.Value<long>();

                case JTokenType.Float:
                    return token.Value<double>();

                case JTokenType.String:
                    return token.Value<string>();

                case JTokenType.Boolean:
                    return token.Value<bool>();

                case JTokenType.Null:
                    return null;

                default:
                    return token.ToString();
            }
        }

        #endregion
    }
}










namespace CommonFoundation
{
    #region ��������

    internal static class Guard
    {
        public static void NotNull<T>(T value, string paramName) where T : class
        {
            if (value == null) throw new ArgumentNullException(paramName);
        }

        public static void NotNullOrEmpty(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Value cannot be null or empty.", paramName);
        }

        public static void NotNullOrWhiteSpace(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }

    #endregion

    #region ͨ��ֵת����

    public interface ITypeConverter
    {
        bool CanConvert(Type sourceType, Type targetType);
        object Convert(object value, Type targetType);
    }

    public interface IValueConverter
    {
        T Convert<T>(object obj);
        bool TryConvert<T>(object obj, out T result);
        void RegisterCustomConverter<TInput, TOutput>(Func<TInput, TOutput> converter);
        void RegisterTypeConverter(ITypeConverter converter);
    }

    internal sealed class DefaultValueConverter : IValueConverter
    {
        private readonly ConcurrentDictionary<(Type From, Type To), Delegate> _customConverters = new();
        private readonly List<ITypeConverter> _typeConverters = new();

        public T Convert<T>(object obj) => TryConvert(obj, out T result) ? result : default;

        public bool TryConvert<T>(object obj, out T result)
        {
            result = default;
            if (obj == null) return true;

            Type targetType = typeof(T);
            Type sourceType = obj.GetType();

            if (obj is T val) { result = val; return true; }

            var key = (sourceType, targetType);
            if (_customConverters.TryGetValue(key, out var del) && del is Func<object, T> typedFunc)
            {
                result = typedFunc(obj);
                return true;
            }

            foreach (var converter in _typeConverters)
            {
                if (converter.CanConvert(sourceType, targetType))
                {
                    result = (T)converter.Convert(obj, targetType);
                    return true;
                }
            }

            Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            try
            {
                result = (T)System.Convert.ChangeType(obj, underlyingType);
                return true;
            }
            catch { return false; }
        }

        public void RegisterCustomConverter<TInput, TOutput>(Func<TInput, TOutput> converter)
        {
            Guard.NotNull(converter, nameof(converter));
            _customConverters[(typeof(TInput), typeof(TOutput))] = (Func<object, TOutput>)(obj => converter((TInput)obj));
        }

        public void RegisterTypeConverter(ITypeConverter converter)
        {
            Guard.NotNull(converter, nameof(converter));
            _typeConverters.Add(converter);
        }
    }

    public static class ValueConverter
    {
        private static readonly IValueConverter _instance = new DefaultValueConverter();
        public static IValueConverter Instance => _instance;
        public static T Convert<T>(object obj) => _instance.Convert<T>(obj);
        public static bool TryConvert<T>(object obj, out T result) => _instance.TryConvert(obj, out result);
        public static void RegisterTypeConverter(ITypeConverter converter) => _instance.RegisterTypeConverter(converter);
    }

    #endregion

    #region ͨ��·��������ģʽƥ��

    public interface IPathParser
    {
        string Separator { get; }
        string[] Split(string path);
        string Combine(params string[] segments);
        string GetParentPath(string path);
        bool IsAncestorOf(string ancestor, string descendant);
        int GetDepth(string path);
    }

    public class DefaultPathParser : IPathParser
    {
        public string Separator { get; }

        public DefaultPathParser(string separator = "/") => Separator = separator;

        public string[] Split(string path) =>
            string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);

        public string Combine(params string[] segments) => string.Join(Separator, segments.Where(s => !string.IsNullOrEmpty(s)));

        public string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int lastIndex = path.LastIndexOf(Separator, StringComparison.Ordinal);
            return lastIndex > 0 ? path.Substring(0, lastIndex) : null;
        }

        public bool IsAncestorOf(string ancestor, string descendant) =>
            descendant != null && ancestor != null && descendant.StartsWith(ancestor + Separator, StringComparison.Ordinal);

        public int GetDepth(string path) => string.IsNullOrEmpty(path) ? 0 : Split(path).Length;
    }

    public interface IPatternMatcher
    {
        bool IsMatch(string input, string pattern);
    }

    public class WildcardPatternMatcher : IPatternMatcher
    {
        private const string DefaultWildcard = "*";
        private readonly string _wildcard;
        private readonly StringComparison _comparison;
        private readonly ConcurrentDictionary<string, Regex> _regexCache = new();

        public WildcardPatternMatcher(string wildcard = DefaultWildcard, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            _wildcard = wildcard;
            _comparison = comparison;
        }

        public bool IsMatch(string input, string pattern)
        {
            if (input == null || pattern == null) return false;
            if (pattern == _wildcard) return true;
            if (!pattern.Contains(_wildcard)) return string.Equals(input, pattern, _comparison);

            var regex = _regexCache.GetOrAdd(pattern, p =>
            {
                string escaped = Regex.Escape(p).Replace(Regex.Escape(_wildcard), ".*");
                var options = _comparison == StringComparison.OrdinalIgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.Compiled : RegexOptions.Compiled;
                return new Regex("^" + escaped + "$", options);
            });
            return regex.IsMatch(input);
        }
    }

    #endregion

    #region �ɹ۲��ֵ䣨�̰߳�ȫ��

    public interface IObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        event Action<TKey, TValue, TValue> ValueChanged;
        void Set(TKey key, TValue value);
        Task SetAsync(TKey key, TValue value);
        Task<bool> TryAddAsync(TKey key, TValue value);
        Task<bool> TryRemoveAsync(TKey key);
    }

    public class ObservableDictionary<TKey, TValue> : IObservableDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _inner = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly object _eventLock = new();
        private Action<TKey, TValue, TValue> _valueChanged;

        public event Action<TKey, TValue, TValue> ValueChanged
        {
            add { lock (_eventLock) _valueChanged += value; }
            remove { lock (_eventLock) _valueChanged -= value; }
        }

        private void RaiseValueChanged(TKey key, TValue oldValue, TValue newValue)
        {
            Action<TKey, TValue, TValue> handler;
            lock (_eventLock) handler = _valueChanged;
            handler?.Invoke(key, oldValue, newValue);
        }

        public void Set(TKey key, TValue value)
        {
            _semaphore.Wait();
            try { SetInternal(key, value); }
            finally { _semaphore.Release(); }
        }

        public async Task SetAsync(TKey key, TValue value)
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try { SetInternal(key, value); }
            finally { _semaphore.Release(); }
        }

        private void SetInternal(TKey key, TValue value)
        {
            bool exists = _inner.TryGetValue(key, out TValue oldValue);
            if (!exists || !EqualityComparer<TValue>.Default.Equals(oldValue, value))
            {
                _inner[key] = value;
                RaiseValueChanged(key, oldValue, value);
            }
        }

        public async Task<bool> TryAddAsync(TKey key, TValue value)
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_inner.ContainsKey(key)) return false;
                _inner.Add(key, value);
                RaiseValueChanged(key, default, value);
                return true;
            }
            finally { _semaphore.Release(); }
        }

        public async Task<bool> TryRemoveAsync(TKey key)
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_inner.Remove(key, out TValue oldValue))
                {
                    RaiseValueChanged(key, oldValue, default);
                    return true;
                }
                return false;
            }
            finally { _semaphore.Release(); }
        }

        public TValue this[TKey key]
        {
            get { _semaphore.Wait(); try { return _inner[key]; } finally { _semaphore.Release(); } }
            set => Set(key, value);
        }

        public ICollection<TKey> Keys { get { _semaphore.Wait(); try { return _inner.Keys.ToArray(); } finally { _semaphore.Release(); } } }
        public ICollection<TValue> Values { get { _semaphore.Wait(); try { return _inner.Values.ToArray(); } finally { _semaphore.Release(); } } }
        public int Count { get { _semaphore.Wait(); try { return _inner.Count; } finally { _semaphore.Release(); } } }
        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value)
        {
            _semaphore.Wait();
            try { _inner.Add(key, value); RaiseValueChanged(key, default, value); }
            finally { _semaphore.Release(); }
        }

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        public void Clear()
        {
            _semaphore.Wait();
            try { _inner.Clear(); }
            finally { _semaphore.Release(); }
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            _semaphore.Wait();
            try { return _inner.Contains(item); }
            finally { _semaphore.Release(); }
        }

        public bool ContainsKey(TKey key)
        {
            _semaphore.Wait();
            try { return _inner.ContainsKey(key); }
            finally { _semaphore.Release(); }
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            _semaphore.Wait();
            try { ((ICollection<KeyValuePair<TKey, TValue>>)_inner).CopyTo(array, arrayIndex); }
            finally { _semaphore.Release(); }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            _semaphore.Wait();
            try { return _inner.ToArray().AsEnumerable().GetEnumerator(); }
            finally { _semaphore.Release(); }
        }

        public bool Remove(TKey key)
        {
            _semaphore.Wait();
            try
            {
                if (_inner.Remove(key, out TValue oldValue))
                {
                    RaiseValueChanged(key, oldValue, default);
                    return true;
                }
                return false;
            }
            finally { _semaphore.Release(); }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            _semaphore.Wait();
            try
            {
                return _inner.TryGetValue(item.Key, out TValue value) &&
                       EqualityComparer<TValue>.Default.Equals(value, item.Value) &&
                       Remove(item.Key);
            }
            finally { _semaphore.Release(); }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            _semaphore.Wait();
            try { return _inner.TryGetValue(key, out value); }
            finally { _semaphore.Release(); }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    #endregion

    #region ͨ�����ڵ�������

    public interface ITreeNode<TValue>
    {
        string Name { get; }
        TValue Value { get; set; }
        ITreeNode<TValue> Parent { get; }
        IEnumerable<ITreeNode<TValue>> Children { get; }
        ITreeNode<TValue> GetChild(string name);
        ITreeNode<TValue> AddChild(string name, TValue value = default);
        bool RemoveChild(string name);
        bool TryGetChild(string name, out ITreeNode<TValue> child);
    }

    public interface ITreeNodeFactory<TValue>
    {
        ITreeNode<TValue> CreateNode(string name, TValue value = default, ITreeNode<TValue> parent = null);
    }

    public class DictionaryTreeNodeFactory<TValue> : ITreeNodeFactory<TValue>
    {
        public ITreeNode<TValue> CreateNode(string name, TValue value = default, ITreeNode<TValue> parent = null) =>
            new DictionaryTreeNode<TValue>(name, value, parent);
    }

    internal class DictionaryTreeNode<TValue> : ITreeNode<TValue>
    {
        private readonly Dictionary<string, ITreeNode<TValue>> _children = new();

        public string Name { get; }
        public TValue Value { get; set; }
        public ITreeNode<TValue> Parent { get; }

        public DictionaryTreeNode(string name, TValue value, ITreeNode<TValue> parent)
        {
            Name = name;
            Value = value;
            Parent = parent;
        }

        public IEnumerable<ITreeNode<TValue>> Children => _children.Values;
        public ITreeNode<TValue> GetChild(string name) => _children.TryGetValue(name, out var child) ? child : null;
        public bool TryGetChild(string name, out ITreeNode<TValue> child) => _children.TryGetValue(name, out child);

        public ITreeNode<TValue> AddChild(string name, TValue value = default)
        {
            if (_children.TryGetValue(name, out var existing)) return existing;
            var child = new DictionaryTreeNode<TValue>(name, value, this);
            _children[name] = child;
            return child;
        }

        public bool RemoveChild(string name) => _children.Remove(name);
    }

    public interface ITreeContainer<TValue>
    {
        TValue GetValue(string path);
        void SetValue(string path, TValue value);
        bool RemoveValue(string path, bool cleanEmptyParents = false);
        IEnumerable<string> GetChildPaths(string parentPath = null);
        IEnumerable<string> GetLeafPaths(string parentPath = null);
        IEnumerable<KeyValuePair<string, TValue>> Flatten();
        ITreeNode<TValue> Root { get; }
    }

    public class TreeContainer<TValue> : ITreeContainer<TValue>
    {
        private readonly ITreeNode<TValue> _root;
        private readonly IPathParser _pathParser;
        private readonly ReaderWriterLockSlim _lock = new();

        public ITreeNode<TValue> Root => _root;

        public TreeContainer(IPathParser pathParser, ITreeNodeFactory<TValue> nodeFactory = null, string rootName = "Root")
        {
            // 必填端口（T）：路径解析器由装配点显式提供；禁 ?? new 默认冒充装配。
            _pathParser = pathParser ?? throw new ArgumentNullException(nameof(pathParser));
            nodeFactory ??= new DictionaryTreeNodeFactory<TValue>();
            _root = nodeFactory.CreateNode(rootName);
        }

        private ITreeNode<TValue> NavigateToNode(string path)
        {
            if (string.IsNullOrEmpty(path)) return _root;
            var parts = _pathParser.Split(path);
            var current = _root;
            foreach (var part in parts)
            {
                if (!current.TryGetChild(part, out current)) return null;
            }
            return current;
        }

        public TValue GetValue(string path)
        {
            _lock.EnterReadLock();
            try { var node = NavigateToNode(path); return node != null ? node.Value : default; }
            finally { _lock.ExitReadLock(); }
        }

        public void SetValue(string path, TValue value)
        {
            Guard.NotNullOrEmpty(path, nameof(path));
            var parts = _pathParser.Split(path);
            if (parts.Length == 0) throw new ArgumentException("Invalid path", nameof(path));

            _lock.EnterWriteLock();
            try
            {
                var current = _root;
                for (int i = 0; i < parts.Length - 1; i++) current = current.AddChild(parts[i]);
                current.AddChild(parts[^1]).Value = value;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemoveValue(string path, bool cleanEmptyParents = false)
        {
            var parentPath = _pathParser.GetParentPath(path);
            if (parentPath == null) return false;

            _lock.EnterWriteLock();
            try
            {
                var parentNode = NavigateToNode(parentPath);
                if (parentNode == null) return false;
                var nodeName = _pathParser.Split(path).LastOrDefault();
                if (string.IsNullOrEmpty(nodeName) || !parentNode.RemoveChild(nodeName)) return false;
                if (cleanEmptyParents) CleanEmptyParents(parentPath);
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        private void CleanEmptyParents(string path)
        {
            var currentPath = path;
            while (!string.IsNullOrEmpty(currentPath))
            {
                var node = NavigateToNode(currentPath);
                if (node == null || node == _root) break;
                if (node.Children.Any() || !EqualityComparer<TValue>.Default.Equals(node.Value, default)) break;

                var parentPath = _pathParser.GetParentPath(currentPath);
                if (parentPath == null) break;
                var parentNode = NavigateToNode(parentPath);
                if (parentNode == null) break;

                var nodeName = _pathParser.Split(currentPath).LastOrDefault();
                if (!string.IsNullOrEmpty(nodeName)) parentNode.RemoveChild(nodeName);
                currentPath = parentPath;
            }
        }

        public IEnumerable<string> GetChildPaths(string parentPath = null)
        {
            _lock.EnterReadLock();
            try
            {
                var parent = NavigateToNode(parentPath ?? string.Empty);
                if (parent == null) yield break;
                foreach (var child in parent.Children)
                    yield return _pathParser.Combine(parentPath, child.Name);
            }
            finally { _lock.ExitReadLock(); }
        }

        public IEnumerable<string> GetLeafPaths(string parentPath = null)
        {
            foreach (var path in GetChildPaths(parentPath))
            {
                var node = NavigateToNode(path);
                if (node != null && !node.Children.Any()) yield return path;
                else foreach (var sub in GetLeafPaths(path)) yield return sub;
            }
        }

        public IEnumerable<KeyValuePair<string, TValue>> Flatten()
        {
            _lock.EnterReadLock();
            try { foreach (var pair in FlattenInternal(_root, string.Empty)) yield return pair; }
            finally { _lock.ExitReadLock(); }
        }

        private IEnumerable<KeyValuePair<string, TValue>> FlattenInternal(ITreeNode<TValue> node, string currentPath)
        {
            if (node == null) yield break;
            if (!node.Children.Any())
            {
                yield return new KeyValuePair<string, TValue>(currentPath, node.Value);
                yield break;
            }
            foreach (var child in node.Children)
            {
                string childPath = string.IsNullOrEmpty(currentPath) ? child.Name : _pathParser.Combine(currentPath, child.Name);
                foreach (var pair in FlattenInternal(child, childPath)) yield return pair;
            }
        }
    }

    #endregion

    #region ͨ�ô�����·��

    public interface IKeyHierarchyProvider<TKey>
    {
        IEnumerable<TKey> GetAncestors(TKey key);
        TKey GetParent(TKey key);
        bool IsMatch(TKey key, TKey pattern);
    }

    public class PathKeyHierarchyProvider : IKeyHierarchyProvider<string>
    {
        private readonly IPathParser _pathParser;
        private readonly IPatternMatcher _patternMatcher;

        public PathKeyHierarchyProvider(IPathParser pathParser, IPatternMatcher patternMatcher)
        {
            _pathParser = pathParser;
            _patternMatcher = patternMatcher;
        }

        public IEnumerable<string> GetAncestors(string key)
        {
            for (var current = key; !string.IsNullOrEmpty(current); current = _pathParser.GetParentPath(current))
            {
                var parent = _pathParser.GetParentPath(current);
                if (parent == null) yield break;
                yield return parent;
            }
        }

        public string GetParent(string key) => _pathParser.GetParentPath(key);
        public bool IsMatch(string key, string pattern) => _patternMatcher.IsMatch(key, pattern);
    }

    public class ProcessingContext<TKey, TInput, TOutput>
    {
        private readonly ConcurrentDictionary<TKey, HandlerEntry> _handlers = new();
        private readonly IKeyHierarchyProvider<TKey> _hierarchyProvider;

        private class HandlerEntry
        {
            public Func<TInput, TOutput> SyncHandler { get; set; }
            public Func<TInput, Task<TOutput>> AsyncHandler { get; set; }
            public bool AllowHierarchy { get; set; }
        }

        public ProcessingContext(IKeyHierarchyProvider<TKey> hierarchyProvider) => _hierarchyProvider = hierarchyProvider;

        public void Register(TKey key, Func<TInput, TOutput> handler, bool overwrite = true, bool allowHierarchy = true)
        {
            Guard.NotNull(handler, nameof(handler));
            var entry = new HandlerEntry { SyncHandler = handler, AllowHierarchy = allowHierarchy };
            if (overwrite) _handlers.AddOrUpdate(key, entry, (_, _) => entry);
            else if (!_handlers.TryAdd(key, entry)) throw new InvalidOperationException($"Key '{key}' already exists.");
        }

        public TOutput Execute(TKey key, TInput input)
        {
            var entry = FindHandlerEntry(key);
            if (entry?.SyncHandler == null) throw new KeyNotFoundException($"No handler for key '{key}'");
            return entry.SyncHandler(input);
        }

        private HandlerEntry FindHandlerEntry(TKey key)
        {
            if (_handlers.TryGetValue(key, out var exact)) return exact;
            foreach (var ancestor in _hierarchyProvider.GetAncestors(key))
                if (_handlers.TryGetValue(ancestor, out var parentEntry) && parentEntry.AllowHierarchy) return parentEntry;
            return null;
        }
    }

    #endregion

    #region ·�������� (PathProcessor)

    public static class PathProcessor
    {
        private const string LABEL_KEY = "label";
        public const char DEFAULT_SEPARATOR = '.';
        public const string DEFAULT_SEPARATOR_STRING = ".";

        public static string PrefabsRootPath { get; set; } = "Prefabs";
        public static string UIPrefabsPath { get; set; } = "Prefabs/UIPrefebs/";

        private static readonly IPathParser _pathParser = new DefaultPathParser(DEFAULT_SEPARATOR_STRING);
        private static readonly IPatternMatcher _patternMatcher = new WildcardPatternMatcher("*", StringComparison.OrdinalIgnoreCase);

        public static string ExtractBaseKeyFromNumberedSuffix(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return fullKey;
            int dotIndex = fullKey.LastIndexOf(DEFAULT_SEPARATOR);
            return dotIndex > 0 && int.TryParse(fullKey.Substring(dotIndex + 1), out _) ? fullKey.Substring(0, dotIndex) : fullKey;
        }

        public static string GetParentKey(string key) => _pathParser.GetParentPath(key);

        public static bool IsPatternMatch(string value, string pattern, string wildcardChar) =>
            !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(pattern) && _patternMatcher.IsMatch(value, pattern);

        public static bool IsKeyMatch(string key, string pattern)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(pattern)) return false;
            return pattern.EndsWith("*") ? key.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                         : string.Equals(key, pattern, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsChildKey(string parentKey, string childKey) => _pathParser.IsAncestorOf(parentKey, childKey);

        public static int GetKeyDepth(string key) => _pathParser.GetDepth(key);

        public static IEnumerable<string> GetChildKeys(IEnumerable<string> allKeys, string parentKey)
        {
            if (allKeys == null || string.IsNullOrEmpty(parentKey)) return Enumerable.Empty<string>();
            string prefix = parentKey + DEFAULT_SEPARATOR_STRING;
            return allKeys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static List<string> GetChildKeys<TValue>(ConcurrentDictionary<string, TValue> dictionary, string parentKey)
        {
            if (dictionary == null || string.IsNullOrEmpty(parentKey)) return new List<string>();
            string prefix = parentKey + DEFAULT_SEPARATOR_STRING;
            return dictionary.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public static string[] SplitKeyParts(string key, string separator = ".")
        {
            if (separator == DEFAULT_SEPARATOR_STRING) return _pathParser.Split(key);
            return string.IsNullOrEmpty(key) ? Array.Empty<string>() : key.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string BuildKeyBySeparator(string[] targetLevels, string[] sourceLevels, Dictionary<int, int> levelMatches, string separator)
        {
            var resultLevels = new string[targetLevels.Length];
            Array.Copy(targetLevels, resultLevels, targetLevels.Length);
            foreach (var match in levelMatches)
                if (match.Key < resultLevels.Length && match.Value < sourceLevels.Length)
                    resultLevels[match.Key] = sourceLevels[match.Value];
            return string.Join(separator, resultLevels);
        }

        public static bool KeyExists<TValue>(ConcurrentDictionary<string, TValue> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrEmpty(key)) return false;
            if (dictionary.ContainsKey(key)) return true;
            int separatorIndex = key.IndexOf(DEFAULT_SEPARATOR);
            return separatorIndex >= 0 && dictionary.ContainsKey(key.Substring(0, separatorIndex));
        }

        public static TValue GetHandler<TValue>(ConcurrentDictionary<string, TValue> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrEmpty(key)) return default;
            if (dictionary.TryGetValue(key, out var handler)) return handler;
            int separatorIndex = key.IndexOf(DEFAULT_SEPARATOR);
            return separatorIndex >= 0 && dictionary.TryGetValue(key.Substring(0, separatorIndex), out handler) ? handler : default;
        }

        public static void CollectLeafPaths(Dictionary<string, object> dict, string currentPath, HashSet<string> paths)
        {
            var container = new TreeContainer<object>(_pathParser);
            ImportNestedDictionary(container.Root, dict);
            foreach (var leaf in container.GetLeafPaths()) paths.Add(leaf);
        }

        private static void ImportNestedDictionary(ITreeNode<object> node, Dictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> nested)
                    ImportNestedDictionary(node.AddChild(kvp.Key), nested);
                else
                    node.AddChild(kvp.Key, kvp.Value);
            }
        }

        public static class PathBuilder
        {
            public static string BuildLabeledPath(Dictionary<string, object> dict, object parameter)
            {
                if (dict == null) return parameter?.ToString() ?? string.Empty;
                dict.TryGetValue(LABEL_KEY, out object labelObj);
                var label = labelObj?.ToString();
                var paramString = parameter?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(label) && label == paramString)
                    throw new ArgumentException("Parameter cannot match label to prevent circular reference.");
                return string.IsNullOrEmpty(label) ? paramString : $"{label}{DEFAULT_SEPARATOR}{paramString}";
            }

            public static string BuildPrefabPath(object get) => $"{PathProcessor.PrefabsRootPath}{DEFAULT_SEPARATOR}{get}";
            public static string BuildResourcePath(object get, string basePath = null) => $"{basePath ?? PathProcessor.UIPrefabsPath}{get}";
            public static string BuildHierarchicalKey(params string[] parts) => _pathParser.Combine(parts);
            public static string CombinePath(params string[] segments) => _pathParser.Combine(segments);
        }

        public static class PathResolver
        {
            private static readonly char[] SeparatorArray = new[] { DEFAULT_SEPARATOR };
            public static string GetParentPath(string path) => _pathParser.GetParentPath(path) ?? string.Empty;
            public static string[] SplitPath(string path) => _pathParser.Split(path);
        }
    }

    #endregion

    #region Ƕ���ֵ��� (NestedDictionaryFramework)

    public static class NestedDictionaryFramework
    {
        public static class CollectionOperations
        {
            public static List<object> GetPathGetAllSubsequentValues(Dictionary<string, object> dict, object get, bool includeNestedDictionaries = false, bool debugpath = false)
            {
                lock (dict)
                {
                    string path = PathProcessor.PathBuilder.BuildLabeledPath(dict, get);
                    object targetNode = DictionaryFrameOperations.GetValueFrame<object>(dict, path);
                    if (targetNode == null) return new List<object>();
                    return targetNode is Dictionary<string, object> nestedDict
                        ? CollectAllValues(nestedDict, includeNestedDictionaries)
                        : new List<object> { targetNode };
                }
            }

            public static List<T> GetPathGetAllSubsequentValues<T>(Dictionary<string, object> dict, object get, bool includeNestedDictionaries = false, bool debugpath = false)
            {
                var raw = GetPathGetAllSubsequentValues(dict, get, includeNestedDictionaries, debugpath);
                var result = new List<T>();
                foreach (var v in raw)
                {
                    if (v == null) continue;
                    if (v is T t) result.Add(t);
                    else if (ValueConverter.TryConvert<T>(v, out var converted)) result.Add(converted);
                }
                return result;
            }

            public static List<KeyValuePair<string, object>> GetPathGetAllSubsequentKeyValuePairs(Dictionary<string, object> dict, object get, bool includeFullPath = false, bool debugpath = false)
            {
                lock (dict)
                {
                    string basePath = PathProcessor.PathBuilder.BuildLabeledPath(dict, get);
                    object targetNode = DictionaryFrameOperations.GetValueFrame<object>(dict, basePath);
                    if (targetNode == null) return new List<KeyValuePair<string, object>>();

                    var result = new List<KeyValuePair<string, object>>();
                    if (targetNode is Dictionary<string, object> nestedDict)
                        CollectKeyValuePairs(nestedDict, result, includeFullPath ? basePath : string.Empty, includeFullPath);
                    else
                        result.Add(new KeyValuePair<string, object>(includeFullPath ? basePath : get?.ToString() ?? string.Empty, targetNode));
                    return result;
                }
            }

            private static List<object> CollectAllValues(Dictionary<string, object> dict, bool includeNestedDictionaries)
            {
                var result = new List<object>();
                if (dict == null) return result;

                var stack = new Stack<Dictionary<string, object>>();
                stack.Push(dict);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    foreach (var kvp in current)
                    {
                        if (kvp.Value is Dictionary<string, object> nested)
                        {
                            stack.Push(nested);
                            if (includeNestedDictionaries) result.Add(nested);
                        }
                        else result.Add(kvp.Value);
                    }
                }
                return result;
            }

            private static void CollectKeyValuePairs(Dictionary<string, object> dict, List<KeyValuePair<string, object>> result, string currentPath, bool includeFullPath)
            {
                if (dict == null) return;
                var stack = new Stack<(Dictionary<string, object> dict, string path)>();
                stack.Push((dict, currentPath));

                while (stack.Count > 0)
                {
                    var (curDict, path) = stack.Pop();
                    foreach (var kvp in curDict)
                    {
                        string keyPath = string.IsNullOrEmpty(path) ? kvp.Key : path + PathProcessor.DEFAULT_SEPARATOR_STRING + kvp.Key;
                        if (kvp.Value is Dictionary<string, object> nested)
                        {
                            stack.Push((nested, keyPath));
                            if (includeFullPath) result.Add(new KeyValuePair<string, object>(keyPath, nested));
                        }
                        else result.Add(new KeyValuePair<string, object>(includeFullPath ? keyPath : kvp.Key, kvp.Value));
                    }
                }
            }
        }

        public static class PathValueOperations
        {
            public static void GetPathSetValue(Dictionary<string, object> dict, object get, object set, bool debugpath = false)
                => PathValueCore<object>(dict, get, debugpath, setValue: set);
            public static T GetPathGetValue<T>(Dictionary<string, object> dict, object get, bool debugpath = false)
                => PathValueCore<T>(dict, get, debugpath);
            public static object GetPathGetValue(Dictionary<string, object> dict, object get, bool debugpath = false)
                => PathValueCore<object>(dict, get, debugpath);
            public static void GetPathRemoveValue(Dictionary<string, object> dict, object get, bool debugpath = false)
                => PathValueCore<object>(dict, get, debugpath, removevalue: true);

            public static T PathValueCore<T>(Dictionary<string, object> dict, object get, bool debugpath = false, object setValue = null, bool removevalue = false)
            {
                lock (dict)
                {
                    string path = PathProcessor.PathBuilder.BuildLabeledPath(dict, get);
                    if (setValue != null)
                    {
                        if (string.IsNullOrEmpty(path)) SimpleKeyValueOperations.SetValue(dict, get?.ToString() ?? string.Empty, setValue);
                        else DictionaryFrameOperations.SetValueFrame(dict, path, setValue);
                        return default;
                    }
                    if (removevalue)
                    {
                        if (string.IsNullOrEmpty(path)) DictionaryFrameOperations.RemoveValueFrame(dict, get?.ToString() ?? string.Empty, debugpath);
                        else DictionaryFrameOperations.RemoveValueFrame(dict, path, debugpath);
                        return default;
                    }
                    return DictionaryFrameOperations.GetValueFrame<T>(dict, path);
                }
            }
        }

        public class VirtualPathValueOperations
        {
            public virtual void GetPathSetValue(Dictionary<string, object> dict, object get, object set, bool debugpath = false)
                => PathValueOperations.PathValueCore<object>(dict, get, debugpath, setValue: set);
            public virtual T GetPathGetValue<T>(Dictionary<string, object> dict, object get, bool debugpath = false)
                => PathValueOperations.PathValueCore<T>(dict, get, debugpath);
        }

        public static class DictionaryFrameOperations
        {
            public static T GetValueFrame<T>(Dictionary<string, object> dict, string path)
            {
                if (dict == null) throw new ArgumentNullException(nameof(dict));
                if (string.IsNullOrEmpty(path)) return default;
                lock (dict)
                {
                    object value = GetNestedValue(dict, path);
                    return ValueConverter.TryConvert(value, out T result) ? result : default;
                }
            }

            public static void SetValueFrame<T>(Dictionary<string, object> dict, string path, T value)
            {
                if (dict == null) throw new ArgumentNullException(nameof(dict));
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
                var keys = PathProcessor.PathResolver.SplitPath(path);
                if (keys.Length == 0) throw new ArgumentException("Path cannot be empty.", nameof(path));

                lock (dict)
                {
                    var current = dict;
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        var key = keys[i];
                        if (!current.TryGetValue(key, out var next) || !(next is Dictionary<string, object> nextDict))
                            current[key] = nextDict = new Dictionary<string, object>();
                        current = nextDict;
                    }
                    current[keys[^1]] = value;
                }
            }

            public static void RemoveValueFrame(Dictionary<string, object> dict, string path, bool debugpath = false)
            {
                if (dict == null || string.IsNullOrWhiteSpace(path)) return;
                var keys = PathProcessor.PathResolver.SplitPath(path);
                if (keys.Length == 0) return;

                lock (dict)
                {
                    var current = dict;
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        if (current.TryGetValue(keys[i], out var next) && next is Dictionary<string, object> nextDict) current = nextDict;
                        else return;
                    }
                    current.Remove(keys[^1]);
                    if (debugpath && keys.Length > 1) CleanEmptyDictionaries(dict, keys);
                }
            }

            private static void CleanEmptyDictionaries(Dictionary<string, object> dict, string[] keys)
            {
                var current = dict;
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (current.TryGetValue(keys[i], out var next) && next is Dictionary<string, object> nextDict && nextDict.Count == 0)
                        current.Remove(keys[i]);
                    else break;
                    current = nextDict;
                }
            }

            private static object GetNestedValue(Dictionary<string, object> dict, string path)
            {
                var keys = PathProcessor.PathResolver.SplitPath(path);
                object current = dict;
                foreach (var key in keys)
                {
                    if (!(current is Dictionary<string, object> currentDict) || !currentDict.TryGetValue(key, out current))
                        return null;
                }
                return current;
            }
        }

        public static class SimpleKeyValueOperations
        {
            public static void SetValue(Dictionary<string, object> storage, string key, object setvalue)
            {
                if (storage == null) throw new ArgumentNullException(nameof(storage));
                lock (storage) storage[key] = setvalue;
            }

            public static T GetValue<T>(Dictionary<string, object> storage, string key)
            {
                if (storage == null) throw new ArgumentNullException(nameof(storage));
                lock (storage)
                {
                    return storage.TryGetValue(key, out object value) && ValueConverter.TryConvert(value, out T result) ? result : default;
                }
            }
        }

        public static class AdvancedOperations
        {
            public static void SetPathValues(Dictionary<string, object> dic, IEnumerable<KeyValuePair<string, object>> settings)
            {
                if (dic == null) throw new ArgumentNullException(nameof(dic));
                foreach (var kv in settings) PathValueOperations.GetPathSetValue(dic, kv.Key, kv.Value);
            }

            public static T ChainAccess<T>(Dictionary<string, object> dict, params string[] paths)
            {
                if (dict == null) throw new ArgumentNullException(nameof(dict));
                lock (dict)
                {
                    object current = dict;
                    foreach (var path in paths)
                    {
                        if (!(current is Dictionary<string, object> currentDict)) return default;
                        current = SimpleKeyValueOperations.GetValue<object>(currentDict, path);
                        if (current == null) break;
                    }
                    return ValueConverter.TryConvert(current, out T result) ? result : default;
                }
            }
        }

        public static class StringOperations
        {
            public static string GetPathGetEntireString(Dictionary<string, object> dic, string key)
                => PathValueOperations.GetPathGetValue<object>(dic, key)?.ToString() ?? string.Empty;

            public static void SetPathEntireString(Dictionary<string, object> dict, object get, object set, bool debugpath = false)
                => PathValueOperations.GetPathSetValue(dict, get, set?.ToString() ?? string.Empty, debugpath);
        }

        public static class FunctionalOperations
        {
            private static T GetOrCreateValue<T>(Dictionary<string, object> dict, object get) where T : new()
                => PathValueOperations.GetPathGetValue<T>(dict, get) ?? new T();

            public static void GetValueSetAction<T>(Dictionary<string, object> dict, object get, Action<T> setAction) where T : new()
            {
                T value = GetOrCreateValue<T>(dict, get);
                setAction?.Invoke(value);
                PathValueOperations.GetPathSetValue(dict, get, value);
            }
        }

        public static class FlowDictionaryOperations
        {
            public static void SetPathForEach<T>(Dictionary<string, object> dict, IEnumerable<T> list, string baseKey,
                Func<int, T, string> keySuffixFunc, Func<int, T, string> valueSuffixFunc = null, bool debugPath = false)
            {
                if (dict == null) throw new ArgumentNullException(nameof(dict));
                if (list == null) throw new ArgumentNullException(nameof(list));
                if (string.IsNullOrEmpty(baseKey)) throw new ArgumentException("Base key cannot be null or empty", nameof(baseKey));
                if (keySuffixFunc == null) throw new ArgumentNullException(nameof(keySuffixFunc));

                int index = 0;
                foreach (var item in list)
                {
                    string fullKey = PathProcessor.PathBuilder.CombinePath(baseKey, keySuffixFunc(index, item));
                    object value = valueSuffixFunc != null ? valueSuffixFunc(index, item) : item;
                    PathValueOperations.GetPathSetValue(dict, fullKey, value, debugPath);
                    index++;
                }
            }
        }
    }

    #endregion

    #region ͳһJSON�޸��� (UnifiedJsonModifier)

    public static class DictionaryExtensions
    {
        public static Dictionary<string, object> DeepClone(this Dictionary<string, object> source)
        {
            if (source == null) return null;
            var clone = new Dictionary<string, object>();
            foreach (var kvp in source)
                clone[kvp.Key] = kvp.Value is Dictionary<string, object> nested ? nested.DeepClone() : kvp.Value;
            return clone;
        }
    }

    public static class DictionaryTraverser
    {
        public static List<KeyValuePair<string, object>> GetLevelKeyValuePairs(Dictionary<string, object> dict, int targetLevel, HashSet<string> keysToSkip = null)
        {
            var result = new List<KeyValuePair<string, object>>();
            if (dict == null) return result;
            keysToSkip ??= new HashSet<string>();

            void Traverse(Dictionary<string, object> current, int currentLevel)
            {
                if (current == null) return;
                foreach (var kvp in current)
                {
                    if (keysToSkip.Contains(kvp.Key)) continue;
                    if (currentLevel == targetLevel) result.Add(kvp);
                    else if (kvp.Value is Dictionary<string, object> nested) Traverse(nested, currentLevel + 1);
                }
            }

            Traverse(dict, 1);
            return result;
        }
    }

    public class UnifiedJsonModifier
    {
        public enum OperationType { ModifyExisting, AddKeyValue, RemoveKey, InsertDictionary, BatchOperation }
        public enum RelativePosition { Before, After, FirstChild, LastChild, Replace }

        public class PositionDescriptor
        {
            public List<string> Path { get; set; } = new List<string>();
            public int? TargetLevel { get; set; }
            public string RelativeToKey { get; set; }
            public RelativePosition PositionType { get; set; } = RelativePosition.After;
            public override string ToString() => Path.Any() ? $"Path: {string.Join(" -> ", Path)}" : (TargetLevel.HasValue ? $"Level: {TargetLevel}" : "Root");
        }

        public class ModificationRule
        {
            public OperationType Type { get; set; }
            public PositionDescriptor Position { get; set; }
            public string OriginalKey { get; set; }
            public string NewKey { get; set; }
            public object NewValue { get; set; }
            public Dictionary<string, object> DictionaryValue { get; set; }
            public bool ApplyToAllMatches { get; set; }
            public override string ToString() => $"{Type}: {OriginalKey} -> {NewKey} at {Position}";
        }

        public class OperationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public Dictionary<string, object> ModifiedDictionary { get; set; }
            public int OperationsPerformed { get; set; }
            public static OperationResult SuccessResult(Dictionary<string, object> dict, int count = 1) => new OperationResult { Success = true, ModifiedDictionary = dict, OperationsPerformed = count };
            public static OperationResult ErrorResult(string message) => new OperationResult { Success = false, Message = message };
        }

        public static OperationResult Modify(Dictionary<string, object> dictionary, List<ModificationRule> rules, HashSet<string> keysToSkip = null)
        {
            if (dictionary == null) return OperationResult.ErrorResult("�����ֵ䲻��Ϊnull");
            if (rules == null || !rules.Any()) return OperationResult.ErrorResult("�޸Ĺ�����Ϊ��");
            keysToSkip ??= new HashSet<string>();

            try
            {
                var result = dictionary.DeepClone();
                int operationsCount = 0;
                foreach (var rule in rules)
                {
                    var ruleResult = ApplySingleRule(result, rule, keysToSkip);
                    if (!ruleResult.Success) return OperationResult.ErrorResult($"����ִ��ʧ��: {rule} - {ruleResult.Message}");
                    operationsCount += ruleResult.OperationsPerformed;
                }
                return OperationResult.SuccessResult(result, operationsCount);
            }
            catch (Exception ex) { return OperationResult.ErrorResult($"�޸Ĺ����з����쳣: {ex.Message}"); }
        }

        public static OperationResult ModifyExisting(Dictionary<string, object> dictionary, string key, object newValue, HashSet<string> keysToSkip = null)
            => Modify(dictionary, new List<ModificationRule> { new ModificationRule { Type = OperationType.ModifyExisting, OriginalKey = key, NewValue = newValue, Position = new PositionDescriptor() } }, keysToSkip);

        public static OperationResult AddKeyValue(Dictionary<string, object> dictionary, PositionDescriptor position, string key, object value, HashSet<string> keysToSkip = null)
            => Modify(dictionary, new List<ModificationRule> { new ModificationRule { Type = OperationType.AddKeyValue, Position = position, NewKey = key, NewValue = value } }, keysToSkip);

        public static OperationResult RemoveKey(Dictionary<string, object> dictionary, PositionDescriptor position, string keyToRemove, HashSet<string> keysToSkip = null)
            => Modify(dictionary, new List<ModificationRule> { new ModificationRule { Type = OperationType.RemoveKey, Position = position, OriginalKey = keyToRemove } }, keysToSkip);

        private static OperationResult ApplySingleRule(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip) => rule.Type switch
        {
            OperationType.ModifyExisting => ApplyModifyExisting(dictionary, rule, keysToSkip),
            OperationType.AddKeyValue => ApplyAddKeyValue(dictionary, rule, keysToSkip),
            OperationType.RemoveKey => ApplyRemoveKey(dictionary, rule, keysToSkip),
            OperationType.InsertDictionary => ApplyInsertDictionary(dictionary, rule, keysToSkip),
            OperationType.BatchOperation => ApplyBatchOperation(dictionary, rule, keysToSkip),
            _ => OperationResult.ErrorResult($"δ֪�Ĳ�������: {rule.Type}")
        };

        private static OperationResult ApplyModifyExisting(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip)
        {
            var positions = FindKeyPositions(dictionary, rule.OriginalKey, keysToSkip);
            if (!positions.Any()) return OperationResult.ErrorResult($"δ�ҵ���: {rule.OriginalKey}");
            int modified = 0;
            foreach (var pos in positions)
            {
                var targetDict = FindDictionaryByPath(dictionary, pos.Path);
                if (targetDict?.ContainsKey(rule.OriginalKey) == true)
                {
                    targetDict[rule.OriginalKey] = rule.NewValue;
                    modified++;
                    if (!rule.ApplyToAllMatches) break;
                }
            }
            return OperationResult.SuccessResult(dictionary, modified);
        }

        private static OperationResult ApplyAddKeyValue(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip)
        {
            var targets = GetTargetDictionaries(dictionary, rule.Position, keysToSkip);
            if (!targets.Any()) return OperationResult.ErrorResult($"δ�ҵ�Ŀ��λ��: {rule.Position}");

            int added = 0;
            foreach (var targetDict in targets)
            {
                bool inserted = false;
                if (string.IsNullOrEmpty(rule.Position.RelativeToKey))
                {
                    if (!targetDict.ContainsKey(rule.NewKey) || rule.Position.PositionType == RelativePosition.Replace)
                    {
                        targetDict[rule.NewKey] = rule.NewValue;
                        inserted = true;
                    }
                }
                else if (InsertRelativeToKey(targetDict, rule.Position, rule.NewKey, rule.NewValue))
                    inserted = true;

                if (inserted)
                {
                    added++;
                    if (!rule.ApplyToAllMatches) break;
                }
            }
            return OperationResult.SuccessResult(dictionary, added);
        }

        private static OperationResult ApplyRemoveKey(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip)
        {
            var positions = FindKeyPositions(dictionary, rule.OriginalKey, keysToSkip);
            if (!positions.Any()) return OperationResult.ErrorResult($"δ�ҵ�Ҫɾ���ļ�: {rule.OriginalKey}");
            int removed = 0;
            foreach (var pos in positions)
            {
                var targetDict = FindDictionaryByPath(dictionary, pos.Path);
                if (targetDict?.Remove(rule.OriginalKey) == true)
                {
                    removed++;
                    if (!rule.ApplyToAllMatches) break;
                }
            }
            return OperationResult.SuccessResult(dictionary, removed);
        }

        private static OperationResult ApplyInsertDictionary(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip)
        {
            if (rule.DictionaryValue == null) return OperationResult.ErrorResult("Ҫ������ֵ䲻��Ϊnull");
            var targets = GetTargetDictionaries(dictionary, rule.Position, keysToSkip);
            if (!targets.Any()) return OperationResult.ErrorResult($"δ�ҵ�Ŀ��λ��: {rule.Position}");

            int inserted = 0;
            foreach (var targetDict in targets)
            {
                if (!targetDict.ContainsKey(rule.NewKey) || rule.Position.PositionType == RelativePosition.Replace)
                {
                    targetDict[rule.NewKey] = rule.DictionaryValue.DeepClone();
                    inserted++;
                    if (!rule.ApplyToAllMatches) break;
                }
            }
            return OperationResult.SuccessResult(dictionary, inserted);
        }

        private static OperationResult ApplyBatchOperation(Dictionary<string, object> dictionary, ModificationRule rule, HashSet<string> keysToSkip)
        {
            if (rule.DictionaryValue?.ContainsKey("rules") != true || !(rule.DictionaryValue["rules"] is List<ModificationRule> batchRules))
                return OperationResult.ErrorResult("����������Ҫ����rules�ֵ���ΪModificationRule�б�");
            return Modify(dictionary, batchRules, keysToSkip);
        }

        private static List<Dictionary<string, object>> GetTargetDictionaries(Dictionary<string, object> dictionary, PositionDescriptor position, HashSet<string> keysToSkip)
        {
            var result = new List<Dictionary<string, object>>();
            if (position.Path.Any())
            {
                var target = FindDictionaryByPath(dictionary, position.Path);
                if (target != null) result.Add(target);
            }
            else if (position.TargetLevel.HasValue)
            {
                var pairs = DictionaryTraverser.GetLevelKeyValuePairs(dictionary, position.TargetLevel.Value, keysToSkip: keysToSkip);
                result.AddRange(pairs.Where(kvp => kvp.Value is Dictionary<string, object>).Select(kvp => kvp.Value as Dictionary<string, object>));
                if (position.TargetLevel.Value == 1) result.Add(dictionary);
            }
            else result.Add(dictionary);
            return result;
        }

        private static Dictionary<string, object> FindDictionaryByPath(Dictionary<string, object> dictionary, List<string> path)
        {
            if (dictionary == null || path == null || !path.Any()) return dictionary;
            var current = dictionary;
            foreach (var segment in path)
            {
                if (current == null || !current.TryGetValue(segment, out var next) || !(next is Dictionary<string, object> nextDict)) return null;
                current = nextDict;
            }
            return current;
        }

        private static List<PositionDescriptor> FindKeyPositions(Dictionary<string, object> dictionary, string targetKey, HashSet<string> keysToSkip)
        {
            var positions = new List<PositionDescriptor>();
            if (dictionary == null || string.IsNullOrEmpty(targetKey)) return positions;

            var stack = new Stack<(Dictionary<string, object> dict, List<string> path)>();
            stack.Push((dictionary, new List<string>()));

            while (stack.Count > 0)
            {
                var (currentDict, currentPath) = stack.Pop();
                if (currentDict.ContainsKey(targetKey) && !keysToSkip.Contains(targetKey))
                    positions.Add(new PositionDescriptor { Path = new List<string>(currentPath) });

                foreach (var kvp in currentDict)
                {
                    if (keysToSkip.Contains(kvp.Key)) continue;
                    if (kvp.Value is Dictionary<string, object> nested)
                        stack.Push((nested, new List<string>(currentPath) { kvp.Key }));
                }
            }
            return positions;
        }

        private static bool InsertRelativeToKey(Dictionary<string, object> dictionary, PositionDescriptor position, string newKey, object newValue)
        {
            if (!dictionary.ContainsKey(position.RelativeToKey)) return false;

            var ordered = dictionary.ToList();
            dictionary.Clear();
            bool inserted = false;
            foreach (var item in ordered)
            {
                if (!inserted && item.Key == position.RelativeToKey)
                {
                    if (position.PositionType == RelativePosition.Before)
                    {
                        dictionary[newKey] = newValue;
                        dictionary[item.Key] = item.Value;
                        inserted = true;
                    }
                    else if (position.PositionType == RelativePosition.After)
                    {
                        dictionary[item.Key] = item.Value;
                        dictionary[newKey] = newValue;
                        inserted = true;
                    }
                }
                else dictionary[item.Key] = item.Value;
            }
            if (!inserted) dictionary[newKey] = newValue;
            return true;
        }
    }

    #endregion

    #region �ֵ䴦���� (DictionaryProcessor)

    public static class DictionaryProcessor
    {
        public class KeyPair
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }

        public class ProcessingResult
        {
            public object Result { get; set; }
            public string Error { get; set; }
            public bool Success { get; set; }
        }

        public class ProcessingContext
        {
            private CommonFoundation.ProcessingContext<string, KeyPair, object> _inner;
            private readonly ConcurrentDictionary<string, byte> _registeredKeys = new(StringComparer.OrdinalIgnoreCase);
            private readonly IKeyHierarchyProvider<string> _hierarchyProvider;

            public ProcessingContext(IEqualityComparer<string> comparer = null)
            {
                var pathParser = new DefaultPathParser(PathProcessor.DEFAULT_SEPARATOR_STRING);
                var patternMatcher = new WildcardPatternMatcher();
                _hierarchyProvider = new PathKeyHierarchyProvider(pathParser, patternMatcher);
                _inner = new CommonFoundation.ProcessingContext<string, KeyPair, object>(_hierarchyProvider);
            }

            public ICollection<string> GetKeys() => _registeredKeys.Keys.ToList();

            public IEnumerable<string> GetChildKeys(string parentKey) => GetKeys().Where(k => PathProcessor.IsChildKey(parentKey, k));

            public void Register(string key, Func<KeyPair, object> processor, bool overwriteIfExists = true, bool handleHierarchy = true)
            {
                if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("������Ϊ null ��հ�", nameof(key));
                if (processor == null) throw new ArgumentNullException(nameof(processor));

                _inner.Register(key, processor, overwriteIfExists, handleHierarchy);
                _registeredKeys.TryAdd(key, 0);
            }

            public void RegisterAction(string key, Action<KeyPair> handler, bool overwriteIfExists = true, bool handleHierarchy = true)
                => Register(key, kp => { handler(kp); return null; }, overwriteIfExists, handleHierarchy);

            public void RegisterRange(IDictionary<string, Func<KeyPair, object>> newHandlers, bool overwriteIfExists = true, bool handleHierarchy = true)
            {
                foreach (var kvp in newHandlers) Register(kvp.Key, kvp.Value, overwriteIfExists, handleHierarchy);
            }

            public bool Remove(string key)
            {
                _registeredKeys.TryRemove(key, out _);
                return true;
            }

            public int RemoveWithChildren(string parentKey)
            {
                if (string.IsNullOrEmpty(parentKey)) return 0;
                var childKeys = GetChildKeys(parentKey).ToList();
                int removed = Remove(parentKey) ? 1 : 0;
                removed += childKeys.Count(k => Remove(k));
                return removed;
            }

            public void Clear()
            {
                _registeredKeys.Clear();
                _inner = new CommonFoundation.ProcessingContext<string, KeyPair, object>(_hierarchyProvider);
            }

            public object Execute(string key, KeyPair keyPair) => _inner.Execute(key, keyPair);

            public bool TryExecute(string key, KeyPair keyPair, out object result, out string error)
            {
                result = null;
                error = null;
                try { result = Execute(key, keyPair); return true; }
                catch (KeyNotFoundException ex) { error = ex.Message; return false; }
                catch (Exception ex) { error = $"������ '{key}' ʱ��������: {ex.Message}"; return false; }
            }

            public IDictionary<string, ProcessingResult> ExecuteRange(IDictionary<string, KeyPair> data, bool continueOnError = false)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                var results = new ConcurrentDictionary<string, ProcessingResult>();
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(data, parallelOptions, kvp =>
                {
                    try { results[kvp.Key] = new ProcessingResult { Result = Execute(kvp.Key, kvp.Value), Success = true }; }
                    catch (Exception ex)
                    {
                        results[kvp.Key] = new ProcessingResult { Error = ex.Message, Success = false };
                        if (!continueOnError) throw new InvalidOperationException($"������ '{kvp.Key}' ʱ��������", ex);
                    }
                });
                return results;
            }
        }

        public static bool Remove(ConcurrentDictionary<string, Func<KeyPair, object>> handlers, string key)
            => !string.IsNullOrEmpty(key) && handlers?.TryRemove(key, out _) == true;

        public static int RemoveWithChildren(ConcurrentDictionary<string, Func<KeyPair, object>> handlers, string parentKey)
        {
            if (handlers == null || string.IsNullOrEmpty(parentKey)) return 0;
            var childKeys = PathProcessor.GetChildKeys(handlers.Keys, parentKey).ToList();
            int removed = handlers.TryRemove(parentKey, out _) ? 1 : 0;
            removed += childKeys.Count(k => handlers.TryRemove(k, out _));
            return removed;
        }

        public static void Clear(ConcurrentDictionary<string, Func<KeyPair, object>> handlers) => handlers?.Clear();

        public static ConcurrentDictionary<string, Func<KeyPair, object>> CreateHandlers(IEqualityComparer<string> comparer = null)
            => new ConcurrentDictionary<string, Func<KeyPair, object>>(comparer ?? StringComparer.OrdinalIgnoreCase);

        public static ProcessingContext CreateContext(IEqualityComparer<string> comparer = null) => new ProcessingContext(comparer);
    }

    #endregion

    #region ��������ģ�� (ParameterCreate)

    public static class ParameterCreate
    {
        public static void SetValueConfig(Dictionary<string, object> dataStorage, Dictionary<string, DictionaryProcessor.KeyPair> ConnectChainInfoKey, int count, List<object> listobj = null)
        {
            var context = DictionaryProcessor.CreateContext();
            if (listobj != null)
                context.RegisterAction("OBJSET", kp => NestedDictionaryFramework.FlowDictionaryOperations.SetPathForEach(dataStorage, listobj, kp?.Value, (index, num) => $"_{index + 1}_"), true, true);

            context.RegisterAction("VALUESET", kp => NestedDictionaryFramework.FlowDictionaryOperations.SetPathForEach(dataStorage,
                CreateObjectList(count, kp?.Value ?? kp?.Key), PathProcessor.ExtractBaseKeyFromNumberedSuffix(kp?.Key), (index, num) => $"_{index + 1}_"), true, true);

            if (ConnectChainInfoKey != null) context.ExecuteRange(ConnectChainInfoKey, false);
        }

        private static List<object> CreateObjectList(int count, string value)
        {
            var list = new List<object>();
            for (int i = 0; i < count; i++) list.Add(value);
            return list;
        }
    }

    #endregion
}
