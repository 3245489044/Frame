
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Modules.ComponentData.Reflection
{
    public class DefaultMemberResolver : IMemberResolver
    {
        public MethodInfo ResolveMethod(InvocationDescriptor desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.TargetType == null) throw new ArgumentException("TargetType cannot be null.", nameof(desc.TargetType));

            var flags = BindingFlags.Public | BindingFlags.NonPublic | (desc.IsStatic ? BindingFlags.Static : BindingFlags.Instance);
            var methods = desc.TargetType.GetMethods(flags)
                .Where(m => m.Name == desc.MemberName)
                .Where(m => m.GetParameters().Length == (desc.ParameterTypes?.Length ?? 0))
                .ToList();

            List<MethodInfo> matchedMethods = new List<MethodInfo>();

            foreach (var method in methods)
            {
                if (method.IsGenericMethod)
                {
                    if (desc.GenericArguments == null || desc.GenericArguments.Length == 0)
                        continue;
                    if (method.GetGenericArguments().Length != desc.GenericArguments.Length)
                        continue;

                    MethodInfo constructed;
                    try
                    {
                        constructed = method.MakeGenericMethod(desc.GenericArguments);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (ParametersMatchExactly(constructed.GetParameters(), desc.ParameterTypes))
                        matchedMethods.Add(constructed);
                }
                else
                {
                    if (desc.GenericArguments != null && desc.GenericArguments.Length > 0)
                        continue;

                    if (ParametersMatchExactly(method.GetParameters(), desc.ParameterTypes))
                        matchedMethods.Add(method);
                }
            }

            if (matchedMethods.Count == 0)
                throw new MissingMethodException(desc.TargetType.FullName, desc.MemberName);
            if (matchedMethods.Count > 1)
                throw new AmbiguousMatchException($"找到多个匹配的方法: {desc.MemberName}，无法确定唯一方法。");

            return matchedMethods[0];
        }

        public PropertyInfo ResolveProperty(InvocationDescriptor desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.TargetType == null) throw new ArgumentException("TargetType cannot be null.", nameof(desc.TargetType));

            var flags = BindingFlags.Public | BindingFlags.NonPublic | (desc.IsStatic ? BindingFlags.Static : BindingFlags.Instance);
            var properties = desc.TargetType.GetProperties(flags)
                .Where(p => p.Name == desc.MemberName)
                .ToList();

            int paramCount = desc.ParameterTypes?.Length ?? 0;
            var candidates = properties.Where(p => p.GetIndexParameters().Length == paramCount).ToList();

            if (candidates.Count == 0)
                throw new MissingMemberException(desc.TargetType.FullName, desc.MemberName);

            if (paramCount == 0)
            {
                if (candidates.Count > 1)
                    throw new AmbiguousMatchException($"找到多个名为 {desc.MemberName} 的属性。");
                return candidates[0];
            }

            List<PropertyInfo> matched = new List<PropertyInfo>();
            foreach (var prop in candidates)
            {
                var indexParams = prop.GetIndexParameters();
                bool match = true;
                for (int i = 0; i < indexParams.Length; i++)
                {
                    if (indexParams[i].ParameterType != desc.ParameterTypes![i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    matched.Add(prop);
            }

            if (matched.Count == 0)
                throw new MissingMemberException(desc.TargetType.FullName, desc.MemberName);
            if (matched.Count > 1)
                throw new AmbiguousMatchException($"找到多个匹配的索引属性: {desc.MemberName}");

            return matched[0];
        }

        public FieldInfo ResolveField(InvocationDescriptor desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.TargetType == null) throw new ArgumentException("TargetType cannot be null.", nameof(desc.TargetType));

            var flags = BindingFlags.Public | BindingFlags.NonPublic | (desc.IsStatic ? BindingFlags.Static : BindingFlags.Instance);
            var field = desc.TargetType.GetField(desc.MemberName!, flags);
            if (field == null)
                throw new MissingMemberException(desc.TargetType.FullName, desc.MemberName);
            return field;
        }

        public ConstructorInfo ResolveConstructor(InvocationDescriptor desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (desc.TargetType == null) throw new ArgumentException("TargetType cannot be null.", nameof(desc.TargetType));

            var ctor = desc.TargetType.GetConstructor(desc.ParameterTypes ?? Type.EmptyTypes);
            if (ctor == null)
                throw new MissingMethodException(desc.TargetType.FullName, ".ctor");
            return ctor;
        }

        private bool ParametersMatchExactly(ParameterInfo[] parameters, Type[]? types)
        {
            if (parameters.Length != (types?.Length ?? 0)) return false;
            if (types == null) return true;
            for (int i = 0; i < types.Length; i++)
                if (parameters[i].ParameterType != types[i]) return false;
            return true;
        }
    }
}
