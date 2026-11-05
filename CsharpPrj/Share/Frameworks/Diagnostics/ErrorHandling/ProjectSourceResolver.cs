
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Common.Diagnostics
{
    public static class ProjectSourceResolver
    {
        private static readonly object _gate = new();

        // 仅在 _gate 内可变；读者只读取 _prefixSnapshot（§4.9）。
        private static Dictionary<string, string[]> _projectPrefixes = new(StringComparer.Ordinal);

        // 不可变快照：发布后不再修改，供异常路径无锁枚举。
        private static volatile Dictionary<string, string[]> _prefixSnapshot = new(StringComparer.Ordinal);

        private static volatile bool _initialized;

        private const string DefaultConfigFileName = "projectPrefixes.json";


        public static void InitializeFromAssemblies(Func<Assembly, bool>? assemblyFilter = null)
        {
            var collected = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (assembly.IsDynamic) continue;
                if (assembly.FullName?.StartsWith("System.") == true) continue;
                if (assembly.FullName?.StartsWith("Microsoft.") == true) continue;
                if (assembly.FullName?.StartsWith("mscorlib") == true) continue;
                if (assembly.FullName?.StartsWith("netstandard") == true) continue;

                if (assemblyFilter != null && !assemblyFilter(assembly)) continue;

                try
                {
                    var exportedTypes = assembly.GetExportedTypes();
                    var namespaces = new HashSet<string>();

                    foreach (var type in exportedTypes)
                    {
                        if (type.Namespace != null)
                        {
                            var firstSegment = type.Namespace.Split('.').FirstOrDefault();
                            if (!string.IsNullOrEmpty(firstSegment))
                            {
                                namespaces.Add(firstSegment);
                            }
                        }
                    }

                    var assemblyName = assembly.GetName().Name;
                    if (assemblyName != null && namespaces.Count > 0)
                    {
                        collected[assemblyName] = namespaces.ToArray();
                    }
                }
                catch
                {
                }
            }

            lock (_gate)
            {
                var merged = new Dictionary<string, string[]>(_projectPrefixes, StringComparer.Ordinal);
                foreach (var kvp in collected)
                {
                    if (merged.TryGetValue(kvp.Key, out var existingPrefixes))
                    {
                        var union = new HashSet<string>(existingPrefixes);
                        union.UnionWith(kvp.Value);
                        merged[kvp.Key] = union.ToArray();
                    }
                    else
                    {
                        merged[kvp.Key] = kvp.Value;
                    }
                }

                _projectPrefixes = merged;
                PublishSnapshot();
                _initialized = true;
            }
        }

        public static bool InitializeFromConfig(string? configPath = null)
        {
            configPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultConfigFileName);

            if (!File.Exists(configPath))
                return false;

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<ProjectPrefixesConfig>(json);
                if (config?.Projects == null)
                    return false;

                lock (_gate)
                {
                    var merged = new Dictionary<string, string[]>(_projectPrefixes, StringComparer.Ordinal);
                    foreach (var kvp in config.Projects)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Length > 0)
                        {
                            merged[kvp.Key] = kvp.Value;
                        }
                    }

                    _projectPrefixes = merged;
                    PublishSnapshot();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PublishSnapshot()
        {
            // 调用方必须已持有 _gate。
            _prefixSnapshot = new Dictionary<string, string[]>(_projectPrefixes, StringComparer.Ordinal);
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_gate)
            {
                if (_initialized) return;
                InitializeFromAssemblies();
            }
        }


        public static void RegisterProject(string projectName, params string[] prefixes)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("项目名不能为空", nameof(projectName));
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("至少需要一个前缀", nameof(prefixes));

            lock (_gate)
            {
                var merged = new Dictionary<string, string[]>(_projectPrefixes, StringComparer.Ordinal)
                {
                    [projectName] = prefixes.ToArray()
                };
                _projectPrefixes = merged;
                PublishSnapshot();
            }
        }

        public static void RegisterProjects(Dictionary<string, string[]> projects)
        {
            projects = PortGuard.Required(projects, nameof(projects));

            lock (_gate)
            {
                var merged = new Dictionary<string, string[]>(_projectPrefixes, StringComparer.Ordinal);
                foreach (var kvp in projects)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Length > 0)
                    {
                        merged[kvp.Key] = kvp.Value.ToArray();
                    }
                }

                _projectPrefixes = merged;
                PublishSnapshot();
            }
        }


        public static string? ResolveFromStackTrace(Exception ex)
        {
            if (ex?.StackTrace == null) return null;

            EnsureInitialized();

            var result = MatchFromText(ex.StackTrace);
            if (result != null) return result;

            var typeName = ex.GetType().FullName;
            if (typeName != null)
            {
                result = MatchFromText(typeName);
                if (result != null) return result;
            }

            if (ex.InnerException != null)
            {
                return ResolveFromStackTrace(ex.InnerException);
            }

            return null;
        }

        private static string? MatchFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // 枚举不可变快照，避免与写入并发时抛“集合已修改”（§4.9）。
            var snapshot = _prefixSnapshot;
            string? bestProject = null;
            var bestLength = -1;

            foreach (var kvp in snapshot)
            {
                foreach (var prefix in kvp.Value)
                {
                    if (string.IsNullOrEmpty(prefix) || prefix.Length <= bestLength) continue;

                    // 命名空间前缀精确匹配，替代 Contains 子串匹配导致的短前缀误判（§4.9）。
                    if (MatchesNamespacePrefix(text, prefix))
                    {
                        bestProject = kvp.Key;
                        bestLength = prefix.Length;
                    }
                }
            }

            return bestProject;
        }

        private static bool MatchesNamespacePrefix(string text, string prefix)
        {
            var index = 0;
            while ((index = text.IndexOf(prefix, index, StringComparison.Ordinal)) >= 0)
            {
                var startOk = index == 0 || !IsIdentifierChar(text[index - 1]);
                var endIndex = index + prefix.Length;
                var endOk = endIndex == text.Length ||
                            text[endIndex] == '.' ||
                            !IsIdentifierChar(text[endIndex]);

                if (startOk && endOk) return true;

                index += prefix.Length;
            }

            return false;
        }

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        public static string? ExtractClassName(Exception ex)
        {
            if (ex?.StackTrace == null) return null;

            var frames = ex.StackTrace.Split('\n');
            foreach (var frame in frames)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    frame.Trim(),
                    @"at\s+([^\s(]+)\.([^\s.(]+)\(([^)]*)\)");
                if (match.Success)
                {
                    var fullTypeName = match.Groups[1].Value;
                    if (!fullTypeName.StartsWith("System.") &&
                        !fullTypeName.StartsWith("Microsoft."))
                    {
                        return fullTypeName;
                    }
                }
            }
            return null;
        }

        public static string? ExtractMethodName(Exception ex)
        {
            if (ex?.StackTrace == null) return null;

            var frames = ex.StackTrace.Split('\n');
            foreach (var frame in frames)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    frame.Trim(),
                    @"at\s+([^\s(]+)\.([^\s.(]+)\(([^)]*)\)");
                if (match.Success)
                {
                    var fullTypeName = match.Groups[1].Value;
                    var methodName = match.Groups[2].Value;
                    if (!fullTypeName.StartsWith("System.") &&
                        !fullTypeName.StartsWith("Microsoft."))
                    {
                        return methodName;
                    }
                }
            }
            return null;
        }


        private sealed class ProjectPrefixesConfig
        {
            public Dictionary<string, string[]>? Projects { get; set; }
        }
    }
}
