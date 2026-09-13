using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RoslynSpace
{
    public static class CompilationReferenceHelper
    {
        private static readonly Lazy<Dictionary<string, HashSet<MetadataReference>>> _referenceMap = new(() =>
        {
            var map = new Dictionary<string, HashSet<MetadataReference>>(StringComparer.OrdinalIgnoreCase);
            void Add(Assembly asm, string[] namespaces)
            {
                try
                {
                    var refr = MetadataReference.CreateFromFile(asm.Location);
                    foreach (var ns in namespaces)
                    {
                        if (!map.TryGetValue(ns, out var set))
                        {
                            set = new HashSet<MetadataReference>(new MetadataReferenceComparer());
                            map[ns] = set;
                        }
                        set.Add(refr);
                    }
                }
                catch { }
            }

            Add(typeof(object).Assembly, new[] { "System", "System.Collections", "System.Reflection" });
            Add(typeof(System.Collections.ArrayList).Assembly, new[] { "System.Collections", "System.Collections.Specialized" });
            Add(typeof(Enumerable).Assembly, new[] { "System.Linq", "System.Linq.Expressions" });
            Add(typeof(System.Threading.Thread).Assembly, new[] { "System.Threading", "System.Threading.Tasks" });
            DiscoverLoadedAssemblies(map);
            return map;
        });

        private static readonly ConcurrentDictionary<string, List<string>> _assemblyToNamespacesCache = new();
        private static readonly ConcurrentDictionary<string, List<MetadataReference>> _namespaceMatchCache = new();
        private static readonly ConcurrentDictionary<string, List<MetadataReference>> _referenceCache = new();

        private static readonly Dictionary<string, string[]> _namespaceAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Web"] = new[] { "System.Web" },
            ["System.Collections.Concurrent"] = new[] { "System.Collections.Concurrent" },
            ["System.Collections.Immutable"] = new[] { "System.Collections.Immutable" },
        };

        private static void DiscoverLoadedAssemblies(Dictionary<string, HashSet<MetadataReference>> map)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(asm => !asm.IsDynamic && !string.IsNullOrEmpty(asm.Location)))
            {
                try
                {
                    var refr = MetadataReference.CreateFromFile(asm.Location);
                    var namespaces = _assemblyToNamespacesCache.GetOrAdd(asm.FullName, _ => GetNamespacesFromAssembly(asm).ToList());
                    foreach (var ns in namespaces)
                    {
                        if (!map.ContainsKey(ns)) map[ns] = new HashSet<MetadataReference>(new MetadataReferenceComparer());
                        map[ns].Add(refr);
                    }
                }
                catch { }
            }
        }

        private static IEnumerable<string> GetNamespacesFromAssembly(Assembly asm)
        {
            var hs = new HashSet<string>();
            try
            {
                foreach (var type in asm.GetTypes().Where(t => !string.IsNullOrEmpty(t.Namespace)))
                {
                    for (var ns = type.Namespace; !string.IsNullOrEmpty(ns); ns = ns.Contains('.') ? ns.Substring(0, ns.LastIndexOf('.')) : null)
                    {
                        hs.Add(ns);
                    }
                }
            }
            catch (ReflectionTypeLoadException rtle)
            {
                foreach (var type in rtle.Types.Where(t => t != null && !string.IsNullOrEmpty(t.Namespace)))
                    hs.Add(type.Namespace);
            }
            catch { }
            return hs;
        }

        public static List<MetadataReference> GetRequiredReferences(string sourceCode)
        {
            if (_referenceCache.TryGetValue(sourceCode, out var cached))
                return cached;

            var usingDirectives = CSharpSourceProvider.Instance.Parse(sourceCode).GetRoot()
                .DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name.ToString()).Distinct().ToList();

            var matched = new HashSet<MetadataReference>(new MetadataReferenceComparer());
            var fixedRefs = GetFixedReferences();

            foreach (var usingNs in usingDirectives)
            {
                if (_namespaceMatchCache.TryGetValue(usingNs, out var cachedRefs))
                {
                    foreach (var r in cachedRefs) matched.Add(r);
                    continue;
                }

                var refs = new List<MetadataReference>();
                var map = _referenceMap.Value;
                if (map.TryGetValue(usingNs, out var exact))
                {
                    refs.AddRange(exact);
                }
                else if (_namespaceAliasMap.TryGetValue(usingNs, out var aliases))
                {
                    foreach (var alias in aliases)
                    {
                        if (map.TryGetValue(alias, out var aliasRefs))
                            refs.AddRange(aliasRefs);
                    }
                }
                else
                {
                    var parents = map.Keys.Where(key => usingNs.StartsWith(key + ".", StringComparison.Ordinal))
                                          .OrderByDescending(key => key.Length);
                    foreach (var parent in parents)
                    {
                        refs.AddRange(map[parent]);
                    }
                }

                if (refs.Count == 0)
                {
                    refs = DiscoverReferencesForNamespace(usingNs).ToList();
                }

                if (refs.Any())
                {
                    _namespaceMatchCache[usingNs] = refs;
                    foreach (var r in refs) matched.Add(r);
                }
            }

            var all = matched.Concat(fixedRefs).Distinct(new MetadataReferenceComparer()).ToList();
            _referenceCache[sourceCode] = all;
            return all;
        }

        private static IEnumerable<MetadataReference> DiscoverReferencesForNamespace(string ns)
        {
            var list = new List<MetadataReference>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(asm => !asm.IsDynamic && !string.IsNullOrEmpty(asm.Location)))
            {
                try
                {
                    var namespaces = GetNamespacesFromAssembly(asm);
                    if (namespaces.Contains(ns) || namespaces.Any(n => ns.StartsWith(n + ".", StringComparison.Ordinal)))
                    {
                        list.Add(MetadataReference.CreateFromFile(asm.Location));
                        foreach (var dep in asm.GetReferencedAssemblies())
                        {
                            try { list.Add(MetadataReference.CreateFromFile(Assembly.Load(dep).Location)); } catch { }
                        }
                    }
                }
                catch { }
            }
            return list.Distinct(new MetadataReferenceComparer());
        }

        private static List<MetadataReference> GetFixedReferences()
        {
            var list = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location)
            };
            try { list.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)); } catch { }
            return list;
        }

        private class MetadataReferenceComparer : IEqualityComparer<MetadataReference>
        {
            public bool Equals(MetadataReference? x, MetadataReference? y) =>
                string.Equals(x?.Display, y?.Display, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode(MetadataReference obj) =>
                obj.Display is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Display);
        }

        public static void ClearCache()
        {
            _referenceCache.Clear();
            _namespaceMatchCache.Clear();
        }
    }
}
