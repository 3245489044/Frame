
#nullable enable

using System.Collections.Concurrent;
using System.Reflection;
using Modules.ComponentData.Reflection;

namespace Modules.GraphProcessing.Utilities
{
    public static class DeepCopyUtility
    {
        public static T DeepCopy<T>(T obj)
        {
            if (obj == null)
                return default!;

            return Force.DeepCloner.DeepClonerExtensions.DeepClone(obj);
        }

    }

    public static class ObjectTraversalUtility
    {
        private static readonly ConcurrentDictionary<Type, (Func<object, object> Getter, Action<object, object> Setter)[]> _accessorsCache = new();

        public static T Traverse<T>(T obj, Func<object, object> visitor, HashSet<object>? visited = null, int maxDepth = 100)
        {
            visited ??= new HashSet<object>();
            return (T)Visit(obj, visitor, visited, 0, maxDepth);
        }

        private static object Visit(object? obj, Func<object, object> visitor, HashSet<object> visited, int depth, int maxDepth)
        {
            if (obj == null || visited.Contains(obj))
                return obj!;

            if (depth > maxDepth)
                throw new InvalidOperationException(
                    $"Object traversal exceeded maximum depth of {maxDepth}. " +
                    "Possible circular reference or deeply nested object graph.");

            visited.Add(obj);


            var resolved = visitor(obj);
            if (!ReferenceEquals(resolved, obj))
                return resolved;

            if (obj is System.Collections.IDictionary dict)
            {
                var newDict = new Dictionary<object, object>();
                foreach (var key in dict.Keys)
                    newDict[key] = Visit(dict[key], visitor, visited, depth + 1, maxDepth);
                return newDict;
            }

            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                    list.Add(Visit(item, visitor, visited, depth + 1, maxDepth));
                return list;
            }

            var type = obj.GetType();
            if (type.IsClass && type != typeof(string))
            {
                var accessors = GetPropertyAccessors(type);
                var originalValues = new Dictionary<int, object>();

                for (int i = 0; i < accessors.Length; i++)
                {
                    var (getter, setter) = accessors[i];
                    var value = getter(obj);
                    originalValues[i] = value;
                    var newValue = Visit(value, visitor, visited, depth + 1, maxDepth);

                    if (!ReferenceEquals(value, newValue))
                    {
                        setter(obj, newValue);
                    }
                }

                return obj;
            }

            return obj;
        }

        private static (Func<object, object> Getter, Action<object, object> Setter)[] GetPropertyAccessors(Type type)
        {
            return _accessorsCache.GetOrAdd(type, t =>
            {
                var properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                    .ToArray();

                var result = new (Func<object, object>, Action<object, object>)[properties.Length];

                for (int i = 0; i < properties.Length; i++)
                {
                    var prop = properties[i];

                    var getterDesc = new InvocationDescriptor
                    {
                        TargetType = t,
                        MemberName = prop.Name,
                        MemberKind = MemberKind.Property,
                        IsStatic = false,
                        IsSetter = false,
                        ParameterTypes = Type.EmptyTypes
                    };
                    var getterDelegate = DelegateCompiler.GetOrCompile(getterDesc, typeof(Func<object, object>));
                    var getter = (Func<object, object>)getterDelegate!;

                    var setterDesc = new InvocationDescriptor
                    {
                        TargetType = t,
                        MemberName = prop.Name,
                        MemberKind = MemberKind.Property,
                        IsStatic = false,
                        IsSetter = true,
                        ParameterTypes = new[] { prop.PropertyType }
                    };
                    var setterDelegate = DelegateCompiler.GetOrCompile(setterDesc, typeof(Action<object, object>));
                    var setter = (Action<object, object>)setterDelegate!;

                    result[i] = (getter, setter);
                }

                return result;
            });
        }
    }
}
