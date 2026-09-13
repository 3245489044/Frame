using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Utilities
{
    public static class UsingConfig
    {
        private static readonly HashSet<string> _fixedUsings = new(StringComparer.OrdinalIgnoreCase)
        {
            "DynamicMethodLookup"
        };

        private static readonly HashSet<string> _fixedStaticUsings = new(StringComparer.OrdinalIgnoreCase)
        {
            "GraphMindStudio.FuncCallFrame.Printer",
        };

        private static readonly HashSet<string> _conditionalUsings = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "UnityEngine", "System.Text", "System.Threading.Tasks", "System.Collections",
            "System.Collections.Generic", "System.Linq", "System.Linq.Expressions", "System.Globalization"
        };

        private static readonly HashSet<string> _customStaticClasses = new(StringComparer.OrdinalIgnoreCase);

        #region 固定普通 Using 配置

        public static IReadOnlyCollection<string> FixedUsings => _fixedUsings;

        public static void AddFixedUsing(string ns)
        {
            if (!string.IsNullOrWhiteSpace(ns))
                _fixedUsings.Add(ns.Trim());
        }

        public static void RemoveFixedUsing(string ns) => _fixedUsings.Remove(ns);

        public static void ClearFixedUsings() => _fixedUsings.Clear();

        #endregion

        #region 固定静态 Using 配置

        public static IReadOnlyCollection<string> FixedStaticUsings => _fixedStaticUsings;

        public static void AddFixedStaticUsing(string type)
        {
            if (!string.IsNullOrWhiteSpace(type))
                _fixedStaticUsings.Add(type.Trim());
        }

        public static void RemoveFixedStaticUsing(string type) => _fixedStaticUsings.Remove(type);

        public static void ClearFixedStaticUsings() => _fixedStaticUsings.Clear();

        #endregion

        #region 条件性 Using 配置

        public static IReadOnlyCollection<string> ConditionalUsings => _conditionalUsings;

        #endregion

        #region 自定义静态类配置

        public static IReadOnlyCollection<string> CustomStaticClasses => _customStaticClasses;

        public static void AddCustomStaticClass(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _customStaticClasses.Add(name.Trim());
        }

        public static void RemoveCustomStaticClass(string name) => _customStaticClasses.Remove(name);

        public static void ClearCustomStaticClasses() => _customStaticClasses.Clear();

        public static IEnumerable<string> GetAllConfiguredStaticClasses() =>
            _fixedStaticUsings.Union(_conditionalUsings).Union(_customStaticClasses).OrderBy(x => x);

        #endregion

        public static void ResetAll()
        {
            _fixedUsings.Clear();
            _fixedUsings.Add("DynamicMethodLookup");
            _fixedStaticUsings.Clear();
            _fixedStaticUsings.Add("GraphMindStudio.FuncCallFrame.Printer");
            _customStaticClasses.Clear();
        }
    }
}
