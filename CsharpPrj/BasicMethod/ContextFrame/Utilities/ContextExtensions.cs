
using System;
using System.Collections.Generic;
using System.Reflection;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Utilities
{
    public static class ContextExtensions
    {

        public static void SetDataRange(this IContext context, IDictionary<string, object?> data)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (data == null) throw new ArgumentNullException(nameof(data));

            foreach (var kvp in data)
            {
                if (kvp.Value != null)
                {
                    var valueType = kvp.Value.GetType();
                    var setDataMethod = GetSetDataMethodInfo(valueType);
                    setDataMethod.Invoke(context, new[] { kvp.Value, kvp.Key });
                }
            }
        }

        public static IDictionary<string, object?> CollectAllData(this IContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new Dictionary<string, object?>();

            if (context.Parent != null)
            {
                var parentData = CollectAllData(context.Parent);
                foreach (var kvp in parentData)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }


            return result;
        }


        public static T CreateChildContext<T>(this IContext parent, string? id = null)
            where T : class, IContext
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            var constructor = typeof(T).GetConstructor(new[] { typeof(string), typeof(IContext) });
            if (constructor != null)
            {
                return (T)constructor.Invoke(new object?[] { id, parent });
            }

            constructor = typeof(T).GetConstructor(new[] { typeof(string) });
            if (constructor != null)
            {
                var child = (T)constructor.Invoke(new object?[] { id });

                var parentProp = typeof(T).GetProperty("Parent", BindingFlags.Public | BindingFlags.Instance);
                if (parentProp != null && parentProp.CanWrite)
                {
                    parentProp.SetValue(child, parent);
                    return child;
                }

                var initMethod = typeof(T).GetMethod("Initialize", new[] { typeof(string), typeof(IContext) });
                if (initMethod != null)
                {
                    initMethod.Invoke(child, new object?[] { id, parent });
                    return child;
                }

                throw new InvalidOperationException(
                    $"类型 {typeof(T).Name} 的 Parent 属性不可写，且没有接受 (string?, IContext?) 的构造函数或 Initialize 方法。");
            }

            throw new InvalidOperationException(
                $"类型 {typeof(T).Name} 没有合适的构造函数。请提供 (string?, IContext?) 或 (string?) 构造函数。");
        }


        public static TConfig.ContextFrame.Abstractions.IContextSnapshot CreateDataSnapshot(this IContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var data = new Dictionary<string, object?>();
            var allData = CollectAllData(context);
            foreach (var kvp in allData)
            {
                data[kvp.Key] = kvp.Value;
            }

            return new ContextSnapshot(context.Id, data);
        }

        public static void RestoreDataFromSnapshot(this IContext context, TConfig.ContextFrame.Abstractions.IContextSnapshot snapshot)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            foreach (var kvp in snapshot.Data)
            {
                if (kvp.Value != null)
                {
                    var valueType = kvp.Value.GetType();
                    var setDataMethod = GetSetDataMethodInfo(valueType);
                    setDataMethod.Invoke(context, new[] { kvp.Value, kvp.Key });
                }
            }
        }


        private static readonly Dictionary<Type, MethodInfo> _setDataMethodCache = new();

        private static MethodInfo GetSetDataMethodInfo(Type valueType)
        {
            if (_setDataMethodCache.TryGetValue(valueType, out var method))
                return method;

            var setDataMethod = typeof(IContext).GetMethod(nameof(IContext.SetData));
            if (setDataMethod == null)
                throw new InvalidOperationException("无法找到 IContext.SetData 方法");

            var genericMethod = setDataMethod.MakeGenericMethod(valueType);
            _setDataMethodCache[valueType] = genericMethod;
            return genericMethod;
        }
    }
}
