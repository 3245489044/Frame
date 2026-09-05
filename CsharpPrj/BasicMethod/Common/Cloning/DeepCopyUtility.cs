
#nullable enable

using System.Collections;
using System.Reflection;

namespace Modules.Common.Cloning
{
    public static class DeepCopyUtility
    {
        public static T DeepCopy<T>(T source) where T : class
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var type = source.GetType();
            if (type.IsValueType || type == typeof(string))
                return source;

            var target = Activator.CreateInstance(type, nonPublic: true);
            if (target == null)
                throw new InvalidOperationException($"Cannot create instance of {type.Name}");

            CopyFields(source, target, type, new HashSet<object>(ReferenceEqualityComparer.Instance));
            return (T)target;
        }

        private static void CopyFields(object source, object target, Type type, HashSet<object> visited)
        {
            if (visited.Contains(source)) return;
            visited.Add(source);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                var value = field.GetValue(source);
                if (value == null) continue;

                if (field.FieldType.IsValueType || field.FieldType == typeof(string))
                {
                    field.SetValue(target, value);
                }
                else if (value is IList list)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType)
                        ?? throw new InvalidOperationException($"Cannot create list of {field.FieldType.Name}");
                    foreach (var item in list)
                    {
                        newList.Add(item is string || (item?.GetType().IsValueType ?? false)
                            ? item
                            : DeepCopy(item as object));
                    }
                    field.SetValue(target, newList);
                }
                else
                {
                    field.SetValue(target, DeepCopy(value));
                }
            }

            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object))
            {
                CopyFields(source, target, baseType, visited);
            }
        }
    }

    internal class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    public static class ObjectTraversalUtility
    {
        public static void Traverse(object root, Action<object, int> visitor, int maxDepth = 10)
        {
            TraverseInternal(root, visitor, 0, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void TraverseInternal(
            object node, Action<object, int> visitor, int depth, int maxDepth, HashSet<object> visited)
        {
            if (node == null || depth > maxDepth || visited.Contains(node))
                return;

            visited.Add(node);
            visitor(node, depth);

            if (node is string || node.GetType().IsValueType)
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var type = node.GetType();
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                var value = field.GetValue(node);
                if (value == null) continue;

                if (value is IList list)
                {
                    foreach (var item in list)
                    {
                        TraverseInternal(item, visitor, depth + 1, maxDepth, visited);
                    }
                }
                else if (!value.GetType().IsValueType && value.GetType() != typeof(string))
                {
                    TraverseInternal(value, visitor, depth + 1, maxDepth, visited);
                }
            }

            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object))
            {
                var baseFields = baseType.GetFields(flags);
                foreach (var field in baseFields)
                {
                    var value = field.GetValue(node);
                    if (value == null) continue;

                    if (!value.GetType().IsValueType && value.GetType() != typeof(string))
                    {
                        TraverseInternal(value, visitor, depth + 1, maxDepth, visited);
                    }
                }
            }
        }

        public static List<T> FindAll<T>(object root, int maxDepth = 10) where T : class
        {
            var results = new List<T>();
            Traverse(root, (node, _) =>
            {
                if (node is T t) results.Add(t);
            }, maxDepth);
            return results;
        }
    }
}
