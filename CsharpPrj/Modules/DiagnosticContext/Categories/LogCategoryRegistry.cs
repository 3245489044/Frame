
using System.Text;
using Common.Diagnostics;

namespace Modules.DiagnosticContext
{

    public class CategoryNode
    {
        public string Name { get; }

        public string FullPath { get; }

        public LogCategory Category { get; }

        public string Description { get; internal set; }

        public CategoryNode? Parent { get; internal set; }

        public List<CategoryNode> Children { get; } = new();

        public bool IsLeaf => Children.Count == 0;

        public CategoryNode(string name, string fullPath, string description, LogCategory category)
        {
            Name = name;
            FullPath = fullPath;
            Description = description;
            Category = category;
        }

        public override string ToString() => FullPath;
    }


    public class CategoryTree
    {
        public List<CategoryNode> Roots { get; } = new();

        public Dictionary<string, CategoryNode> FlatMap { get; } = new();

        public string ToTreeString()
        {
            var sb = new StringBuilder();
            foreach (var root in Roots)
            {
                AppendNode(sb, root, 0);
            }
            return sb.ToString();
        }

        private static void AppendNode(StringBuilder sb, CategoryNode node, int depth)
        {
            var indent = new string(' ', depth * 2);
            var suffix = node.IsLeaf ? "" : "/";
            sb.AppendLine($"{indent}{node.Name}{suffix}  ({node.Description})");
            foreach (var child in node.Children)
            {
                AppendNode(sb, child, depth + 1);
            }
        }
    }


    public static class LogCategoryRegistry
    {
        private static Dictionary<string, CategoryNode>? _nodes;
        private static readonly object _initLock = new();

        private static Dictionary<string, LogCategory>? _moduleAllMasks;

        private static Dictionary<string, LogCategory>? _enumNameMap;

        static LogCategoryRegistry()
        {
            LoggingSetup.OnBeforeInitialize += InitializeDefaults;
            LoggingSetup.ResolveCategoryDisplayName += GetDisplayName;
            LoggingSetup.ResolveEnumName += ResolveEnumName;
        }


        private static Dictionary<string, CategoryNode>? _dynamicNodes;
        private static ulong _nextDynamicBit = 1UL << 16;


        public const string VirtualRoot = "/logs";

        private static void EnsureInitialized()
        {
            if (_nodes != null) return;

            lock (_initLock)
            {
                if (_nodes != null) return;
                _nodes = BuildCategoryTree();
            }
        }

        private static Dictionary<string, CategoryNode> BuildCategoryTree()
        {
            var nodes = new Dictionary<string, CategoryNode>();
            var enumNameMap = new Dictionary<string, LogCategory>(StringComparer.OrdinalIgnoreCase);
            var moduleAllMasks = new Dictionary<string, LogCategory>(StringComparer.OrdinalIgnoreCase);

            ulong nextBit = 1UL << 16;

            var definitions = LogCategoryLoader.LoadDefinitions();

            foreach (var (path, enumName, description) in definitions)
            {
                var category = (LogCategory)nextBit;
                nextBit <<= 1;

                if (nextBit == 0)
                    throw new InvalidOperationException("Log category bit flags exhausted (max 48 module categories)");

                var segments = path.Split('/');
                var nodeName = segments[segments.Length - 1];
                var node = new CategoryNode(nodeName, path, description, category);

                nodes[path] = node;
                enumNameMap[enumName] = category;

                var moduleName = segments[0];
                if (moduleAllMasks.TryGetValue(moduleName, out var existingMask))
                    moduleAllMasks[moduleName] = existingMask | category;
                else
                    moduleAllMasks[moduleName] = category;

                if (segments.Length > 1)
                {
                    var parentPath = string.Join("/", segments.Take(segments.Length - 1));
                    if (!nodes.TryGetValue(parentPath, out var parent))
                    {
                        var parentName = segments[segments.Length - 2];
                        parent = new CategoryNode(parentName, parentPath, parentName, LogCategory.None);
                        nodes[parentPath] = parent;
                    }
                    parent.Children.Add(node);
                    node.Parent = parent;
                }
            }

            _enumNameMap = enumNameMap;
            _moduleAllMasks = moduleAllMasks;

            _nextDynamicBit = nextBit;

            return nodes;
        }

        public static LogCategory ResolveEnumName(string enumName)
        {
            if (string.IsNullOrWhiteSpace(enumName))
                return LogCategory.None;

            EnsureInitialized();

            if (_enumNameMap != null && _enumNameMap.TryGetValue(enumName, out var mappedCategory))
                return mappedCategory;

            if (Enum.TryParse<LogCategory>(enumName, ignoreCase: true, out var category))
                return category;

            var lastUnderscore = enumName.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < enumName.Length - 1)
            {
                var moduleName = enumName.Substring(0, lastUnderscore);
                var catName = enumName.Substring(lastUnderscore + 1);
                var path = $"{moduleName}/{catName}";

                try
                {
                    var node = Register(path, $"Auto-registered: {enumName}");
                    return node.Category;
                }
                catch
                {
                }
            }

            return LogCategory.None;
        }

        public static string GetDisplayName(LogCategory category)
        {
            if (Enum.IsDefined(typeof(LogCategory), category))
                return category.ToString();

            EnsureInitialized();

            if (_enumNameMap != null)
            {
                foreach (var kvp in _enumNameMap)
                {
                    if (kvp.Value == category)
                        return kvp.Key;
                }
            }

            var allNodes = GetAllNodes();
            var node = allNodes.Values.FirstOrDefault(n => n.Category == category);
            if (node != null)
            {
                return node.FullPath.Replace('/', '_');
            }

            return $"0x{(ulong)(object)category:X}";
        }

        public static LogCategory GetModuleMask(string moduleName)
        {
            EnsureInitialized();

            if (_moduleAllMasks != null && _moduleAllMasks.TryGetValue(moduleName, out var mask))
                return mask;

            return LogCategory.None;
        }

        public static IReadOnlyDictionary<string, LogCategory> GetEnumNameMap()
        {
            EnsureInitialized();
            return _enumNameMap ?? new Dictionary<string, LogCategory>();
        }


        public static CategoryNode Register(string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            var normalized = StripVirtualRoot(path);
            EnsureInitialized();

            lock (_initLock)
            {
                _dynamicNodes ??= new();

                if (_nodes!.ContainsKey(normalized))
                {
                    var existingNode = _nodes[normalized];
                    existingNode.Description = description;
                    _dynamicNodesVersion++;
                    return existingNode;
                }
                if (_dynamicNodes.ContainsKey(normalized))
                {
                    var existingDynNode = _dynamicNodes[normalized];
                    existingDynNode.Description = description;
                    _dynamicNodesVersion++;
                    return existingDynNode;
                }

                if (_nextDynamicBit == 0 || _nextDynamicBit > (1UL << 63))
                    throw new InvalidOperationException("No more dynamic bit flags available");
                var flag = _nextDynamicBit;
                _nextDynamicBit <<= 1;

                var segments = normalized.Split('/');
                var nodeName = segments[segments.Length - 1];
                var node = new CategoryNode(nodeName, normalized, description, (LogCategory)flag);

                _dynamicNodes[normalized] = node;

                if (segments.Length > 1)
                {
                    var parentPath = string.Join("/", segments.Take(segments.Length - 1));
                    var parent = EnsureDynamicParentPath(parentPath);
                    parent.Children.Add(node);
                    node.Parent = parent;
                }

                _dynamicNodesVersion++;
                return node;
            }
        }

        public static void RegisterRange(params (string path, string description)[] categories)
        {
            foreach (var (path, description) in categories)
            {
                Register(path, description);
            }
        }

        public static void RegisterModule(string moduleName, params (string name, string description)[] categories)
        {
            foreach (var (name, description) in categories)
            {
                Register($"{moduleName}/{name}", description);
            }
        }

        public static LogCategory Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return LogCategory.None;

            var normalized = StripVirtualRoot(path);
            EnsureInitialized();

            var allNodes = GetAllNodes();
            if (allNodes.TryGetValue(normalized, out var node))
                return node.Category;

            return LogCategory.None;
        }


        public static ulong ResolveAsFlag(string path)
        {
            return (ulong)Resolve(path);
        }

        public static ulong ResolveCombined(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0;

            ulong result = 0;
            var parts = path.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                result |= (ulong)Resolve(part);
            }
            return result;
        }

        public static IEnumerable<string> ListDirectories(string path)
        {
            var normalized = StripVirtualRoot(path);
            EnsureInitialized();

            var allNodes = GetAllNodes();

            if (string.IsNullOrEmpty(normalized))
            {
                return allNodes.Values
                    .Where(n => n.Parent == null)
                    .Select(n => n.Name + "/");
            }

            if (allNodes.TryGetValue(normalized, out var node))
            {
                return node.Children.Select(c => c.Name + (c.IsLeaf ? "" : "/"));
            }

            return Enumerable.Empty<string>();
        }

        public static CategoryNode? GetNode(string path)
        {
            var normalized = StripVirtualRoot(path);
            EnsureInitialized();

            var allNodes = GetAllNodes();
            if (allNodes.TryGetValue(normalized, out var node))
                return node;

            return null;
        }


        public static IEnumerable<CategoryNode> GetLeafNodes()
        {
            EnsureInitialized();
            var allNodes = GetAllNodes();
            return allNodes.Values.Where(n => n.IsLeaf).ToList();
        }

        public static CategoryTree GetTree()
        {
            EnsureInitialized();

            var allNodes = GetAllNodes();
            var tree = new CategoryTree();
            foreach (var root in allNodes.Values.Where(n => n.Parent == null))
            {
                tree.Roots.Add(root);
            }
            foreach (var kvp in allNodes)
            {
                tree.FlatMap[kvp.Key] = kvp.Value;
            }
            return tree;
        }

        public static string GetTreeJson()
        {
            var tree = GetTree();
            var dict = new Dictionary<string, object?>();
            foreach (var root in tree.Roots)
            {
                BuildNodeJson(root, dict);
            }
            return System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        private static void BuildNodeJson(CategoryNode node, Dictionary<string, object?> parent)
        {
            if (node.IsLeaf)
            {
                parent[node.Name] = node.Description;
            }
            else
            {
                var children = new Dictionary<string, object?>();
                foreach (var child in node.Children)
                {
                    BuildNodeJson(child, children);
                }
                parent[node.Name + "/"] = children;
            }
        }

        public static int Count
        {
            get
            {
                EnsureInitialized();
                var allNodes = GetAllNodes();
                return allNodes.Count;
            }
        }

        public static IEnumerable<string> GetAllPaths()
        {
            EnsureInitialized();
            var allNodes = GetAllNodes();
            return allNodes.Keys.OrderBy(k => k).ToList();
        }

        public static bool IsRegistered(string path)
        {
            var normalized = StripVirtualRoot(path);
            EnsureInitialized();

            var allNodes = GetAllNodes();
            return allNodes.ContainsKey(normalized);
        }

        public static string? GetPath(ulong bitFlag)
        {
            EnsureInitialized();
            var category = (LogCategory)bitFlag;

            var allNodes = GetAllNodes();
            var node = allNodes.Values.FirstOrDefault(n => n.Category == category);
            return node?.FullPath;
        }


        public static void InitializeDefaults()
        {
            lock (_initLock)
            {
                _nodes = BuildCategoryTree();
            }
        }

        internal static void Clear()
        {
            lock (_initLock)
            {
                _nodes = new Dictionary<string, CategoryNode>();
                _dynamicNodes = null;
                _enumNameMap = null;
                _moduleAllMasks = null;
                _nextDynamicBit = 1UL << 16;
            }
        }



        private static Dictionary<string, CategoryNode>? _mergedNodesCache;
        private static volatile int _dynamicNodesVersion;

        private static volatile int _currentCacheVersion;

        private static Dictionary<string, CategoryNode> GetAllNodes()
        {
            lock (_initLock)
            {
                if (_dynamicNodes == null || _dynamicNodes.Count == 0)
                    return _nodes!;

                if (_mergedNodesCache != null && _currentCacheVersion == _dynamicNodesVersion)
                    return _mergedNodesCache;

                var merged = new Dictionary<string, CategoryNode>(_nodes!);
                foreach (var kvp in _dynamicNodes)
                {
                    merged[kvp.Key] = kvp.Value;
                }

                _mergedNodesCache = merged;
                _currentCacheVersion = _dynamicNodesVersion;

                return merged;
            }
        }

        private static CategoryNode EnsureDynamicParentPath(string parentPath)
        {
            if (_nodes!.TryGetValue(parentPath, out var existingStatic))
                return existingStatic;

            if (_dynamicNodes!.TryGetValue(parentPath, out var existing))
                return existing;

            var segments = parentPath.Split('/');
            var nodeName = segments[segments.Length - 1];
            var node = new CategoryNode(nodeName, parentPath, nodeName, LogCategory.None);

            _dynamicNodes[parentPath] = node;

            if (segments.Length > 1)
            {
                var grandParentPath = string.Join("/", segments.Take(segments.Length - 1));
                var parent = EnsureDynamicParentPath(grandParentPath);
                parent.Children.Add(node);
                node.Parent = parent;
            }

            return node;
        }

        private static string StripVirtualRoot(string path)

        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = path.Trim('/').Replace('\\', '/');

            if (normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(5);
            else if (normalized.Equals("logs", StringComparison.OrdinalIgnoreCase))
                normalized = string.Empty;

            return normalized;
        }
    }
}
