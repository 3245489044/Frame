
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
        private static readonly Dictionary<string, string[]> _projectPrefixes = new();

        private static bool _initialized;

        private const string DefaultConfigFileName = "projectPrefixes.json";


        public static void InitializeFromAssemblies(Func<Assembly, bool>? assemblyFilter = null)
        {
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
                        if (_projectPrefixes.TryGetValue(assemblyName, out var existingPrefixes))
                        {
                            var merged = new HashSet<string>(existingPrefixes);
                            merged.UnionWith(namespaces);
                            _projectPrefixes[assemblyName] = merged.ToArray();
                        }
                        else
                        {
                            _projectPrefixes[assemblyName] = namespaces.ToArray();
                        }
                    }
                }
                catch
                {
                }
            }

            _initialized = true;
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

                foreach (var kvp in config.Projects)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Length > 0)
                    {
                        _projectPrefixes[kvp.Key] = kvp.Value;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                InitializeFromAssemblies();
            }
        }


        public static void RegisterProject(string projectName, params string[] prefixes)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("项目名不能为空", nameof(projectName));
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("至少需要一个前缀", nameof(prefixes));

            _projectPrefixes[projectName] = prefixes;
        }

        public static void RegisterProjects(Dictionary<string, string[]> projects)
        {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            foreach (var kvp in projects)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Length > 0)
                {
                    _projectPrefixes[kvp.Key] = kvp.Value;
                }
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
            foreach (var kvp in _projectPrefixes)
            {
                foreach (var prefix in kvp.Value)
                {
                    if (text.Contains(prefix))
                    {
                        return kvp.Key;
                    }
                }
            }

            return null;
        }

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
