#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Common.Models;

namespace BasicAlgorithm
{
    public static class DependencyGraphAlgorithms
    {
        public static DependencyGraph AnalyzeDependencyGraph(this IEnumerable<ReferenceRelation> relations)
        {
            var graph = new DependencyGraph();
            graph.Relations = relations.ToList();
            foreach (var rel in relations)
            {
                graph.AddNode(rel.SourceModuleId);
                graph.AddNode(rel.TargetModuleId);
                graph.AddEdge(rel.SourceModuleId, rel.TargetModuleId);
            }
            return graph;
        }

        public static List<CircularDependency> DetectCircularDependencies(this DependencyGraph graph)
        {
            var cycles = new List<CircularDependency>();
            var visited = new HashSet<string>();
            foreach (var moduleId in graph.Relations.Select(r => r.SourceModuleId).Distinct())
            {
                if (!visited.Contains(moduleId))
                    HasCycle(moduleId, graph, visited, new HashSet<string>(), new List<string>(), cycles);
            }
            return cycles;
        }

        public static bool HasCycle(this DependencyGraph graph)
        {
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            foreach (var moduleId in graph.Relations.Select(r => r.SourceModuleId).Distinct())
            {
                if (!visited.Contains(moduleId))
                {
                    if (HasCycleInternal(moduleId, graph, visited, inStack))
                        return true;
                }
            }
            return false;
        }

        public static List<ReferenceRelation> GetDependenciesRecursive(
            this IEnumerable<ReferenceRelation> relations,
            string resourceId,
            int maxDepth)
        {
            var result = new List<ReferenceRelation>();
            var visited = new HashSet<string>();
            GetDependenciesRecursiveInternal(resourceId, maxDepth, 0, visited, result, relations);
            return result;
        }

        #region Private Helpers

        private static bool HasCycle(
            string current,
            DependencyGraph graph,
            HashSet<string> visited,
            HashSet<string> inStack,
            List<string> path,
            List<CircularDependency> cycles)
        {
            if (inStack.Contains(current))
            {
                int start = path.IndexOf(current);
                if (start != -1)
                    cycles.Add(new CircularDependency { ModuleIds = path.Skip(start).ToList(), Depth = path.Count - start });
                return true;
            }
            if (visited.Contains(current)) return false;

            visited.Add(current);
            inStack.Add(current);
            path.Add(current);

            foreach (var dep in graph.Relations.Where(r => r.SourceModuleId == current).Select(r => r.TargetModuleId))
            {
                if (HasCycle(dep, graph, visited, inStack, path, cycles))
                    return true;
            }

            inStack.Remove(current);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static bool HasCycleInternal(
            string current,
            DependencyGraph graph,
            HashSet<string> visited,
            HashSet<string> inStack)
        {
            if (inStack.Contains(current))
                return true;
            if (visited.Contains(current))
                return false;

            visited.Add(current);
            inStack.Add(current);

            foreach (var dep in graph.Relations.Where(r => r.SourceModuleId == current).Select(r => r.TargetModuleId))
            {
                if (HasCycleInternal(dep, graph, visited, inStack))
                    return true;
            }

            inStack.Remove(current);
            return false;
        }

        private static void GetDependenciesRecursiveInternal(
            string current,
            int maxDepth,
            int currentDepth,
            HashSet<string> visited,
            List<ReferenceRelation> result,
            IEnumerable<ReferenceRelation> relations)
        {
            if (currentDepth >= maxDepth || visited.Contains(current))
                return;
            visited.Add(current);
            var deps = relations.Where(r => r.SourceModuleId == current).ToList();
            foreach (var dep in deps)
            {
                result.Add(dep);
                GetDependenciesRecursiveInternal(dep.TargetModuleId, maxDepth, currentDepth + 1, visited, result, relations);
            }
        }

        #endregion
    }
}
