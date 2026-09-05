#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Modules.ReflectionCommon
{
    public static class TypeExtensions
    {
        private static List<T> GetMembersRecursive<T>(Type type, Func<Type, BindingFlags, T[]> getter, BindingFlags flags)
        {
            var members = new List<T>();
            var current = type;
            while (current != null && current != typeof(object))
            {
                members.AddRange(getter(current, flags));
                current = current.BaseType;
            }
            return members;
        }

        public static FieldInfo[] GetAllFields(this Type type, bool includePrivate = true)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (includePrivate)
                flags |= BindingFlags.NonPublic;
            return GetMembersRecursive(type, (t, f) => t.GetFields(f), flags).ToArray();
        }

        public static PropertyInfo[] GetAllProperties(this Type type, bool includePrivate = true)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (includePrivate)
                flags |= BindingFlags.NonPublic;
            return GetMembersRecursive(type, (t, f) => t.GetProperties(f), flags).ToArray();
        }

        public static MethodInfo[] GetAllMethods(this Type type, bool includePrivate = true)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static;
            if (includePrivate)
                flags |= BindingFlags.NonPublic;
            return GetMembersRecursive(type, (t, f) => t.GetMethods(f), flags).ToArray();
        }

        public static bool Implements<TInterface>(this Type type) where TInterface : class
        {
            return typeof(TInterface).IsAssignableFrom(type);
        }

        public static bool InheritsFrom<TBase>(this Type type) where TBase : class
        {
            return typeof(TBase).IsAssignableFrom(type) && type != typeof(TBase);
        }

        public static string GetFriendlyName(this Type type)
        {
            if (type == null)
                return "null";

            if (type.IsGenericType)
            {
                var name = type.Name.Split('`')[0];
                var args = type.GetGenericArguments().Select(GetFriendlyName);
                return $"{name}<{string.Join(", ", args)}>";
            }

            if (type.IsArray)
            {
                return $"{GetFriendlyName(type.GetElementType()!)}[]";
            }

            return type.Name switch
            {
                "String" => "string",
                "Int32" => "int",
                "Int64" => "long",
                "Boolean" => "bool",
                "Double" => "double",
                "Single" => "float",
                "Decimal" => "decimal",
                "Void" => "void",
                "Object" => "object",
                _ => type.Name
            };
        }

        public static bool TryGetPropertyValue(this object obj, string propertyName, out object? value)
        {
            value = null;

            try
            {
                var type = obj.GetType();
                var property = type.GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property != null && property.CanRead)
                {
                    value = property.GetValue(obj);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetPropertyValue(this object obj, string propertyName, object? value)
        {
            try
            {
                var type = obj.GetType();
                var property = type.GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property != null && property.CanWrite)
                {
                    property.SetValue(obj, value);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetFieldValue(this object obj, string fieldName, out object? value)
        {
            value = null;

            try
            {
                var type = obj.GetType();
                var field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    value = field.GetValue(obj);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetFieldValue(this object obj, string fieldName, object? value)
        {
            try
            {
                var type = obj.GetType();
                var field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    field.SetValue(obj, value);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static object? CreateInstance(this Type type, params object?[]? args)
        {
            try
            {
                if (args == null || args.Length == 0)
                {
                    return Activator.CreateInstance(type);
                }

                return Activator.CreateInstance(type, args);
            }
            catch
            {
                return null;
            }
        }

        public static TAttribute? GetAttribute<TAttribute>(this Type type, bool inherit = true)
            where TAttribute : Attribute
        {
            return type.GetCustomAttribute<TAttribute>(inherit);
        }

        public static TAttribute[] GetAttributes<TAttribute>(this Type type, bool inherit = true)
            where TAttribute : Attribute
        {
            return type.GetCustomAttributes<TAttribute>(inherit).ToArray();
        }

        public static bool IsNullableType(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        public static Type GetUnderlyingType(this Type type)
        {
            if (type.IsNullableType())
                return Nullable.GetUnderlyingType(type)!;

            return type;
        }

        public static bool IsCollectionType(this Type type)
        {
            if (type == typeof(string))
                return false;

            return type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }

        public static Type? GetCollectionElementType(this Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments()[0];
        }

        public static readonly HashSet<Type> NumericTypes = new HashSet<Type>
        {
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(short), typeof(ushort), typeof(byte), typeof(sbyte)
        };

        public static bool IsNumeric(this Type type)
        {
            return NumericTypes.Contains(type) ||
                   (Nullable.GetUnderlyingType(type) != null && NumericTypes.Contains(Nullable.GetUnderlyingType(type)!));
        }


        public static bool IsAsyncType(this Type returnType)
        {
            return returnType == typeof(Task) ||
                   (returnType.IsGenericType &&
                    returnType.GetGenericTypeDefinition() == typeof(Task<>));
        }

        public static MethodInfo? FindMethodInfo(this Assembly assembly, string className, string? nestedClassName, string methodName)
        {
            var fullClassName = nestedClassName != null
                ? $"{className}+{nestedClassName}"
                : className;

            var type = assembly.GetType(fullClassName);
            if (type == null)
            {
                type = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == className);
            }

            if (type == null)
                return null;

            return type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
        }

        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeof(object);

            try
            {
                var type = Type.GetType(typeName);
                if (type != null)
                    return type;
            }
            catch
            {
            }

            return typeName.ToLower() switch
            {
                "void" => typeof(void),
                "int" or "system.int32" => typeof(int),
                "long" or "system.int64" => typeof(long),
                "float" or "system.single" => typeof(float),
                "double" or "system.double" => typeof(double),
                "bool" or "system.boolean" => typeof(bool),
                "string" or "system.string" => typeof(string),
                "char" or "system.char" => typeof(char),
                "byte" or "system.byte" => typeof(byte),
                "short" or "system.int16" => typeof(short),
                "uint" or "system.uint32" => typeof(uint),
                "ulong" or "system.uint64" => typeof(ulong),
                "ushort" or "system.uint16" => typeof(ushort),
                "sbyte" or "system.sbyte" => typeof(sbyte),
                "object" or "system.object" => typeof(object),
                "decimal" or "system.decimal" => typeof(decimal),
                "task" or "system.threading.tasks.task" => typeof(Task),
                _ => typeof(object)
            };
        }
    }
}


