
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Modules.ComponentData.Reflection
{
    public static class DelegateCompiler
    {
        private static readonly ConcurrentDictionary<(InvocationDescriptor, Type), Delegate> _cache = new ConcurrentDictionary<(InvocationDescriptor, Type), Delegate>();
        private static readonly IMemberResolver _resolver = new DefaultMemberResolver();

        public static Delegate GetOrCompile(InvocationDescriptor desc, Type delegateType)
        {
            return _cache.GetOrAdd((desc, delegateType), key => Compile(key.Item1, key.Item2));
        }

        private static Delegate Compile(InvocationDescriptor desc, Type delegateType)
        {
            var invokeMethod = delegateType.GetMethod("Invoke")!;
            var delegateParams = invokeMethod.GetParameters();
            Type returnType = invokeMethod.ReturnType;

            ParameterExpression[] parameters = new ParameterExpression[delegateParams.Length];
            for (int i = 0; i < delegateParams.Length; i++)
                parameters[i] = Expression.Parameter(delegateParams[i].ParameterType, $"p{i}");

            Expression? instance = null;
            Expression[] args;

            if (desc.MemberKind == MemberKind.Constructor)
            {
                args = parameters;
            }
            else
            {
                if (!desc.IsStatic)
                {
                    if (parameters.Length == 0)
                        throw new ArgumentException("缺少实例参数");
                    instance = parameters[0];
                    args = parameters.Skip(1).ToArray();
                }
                else
                {
                    args = parameters;
                }
            }

            Expression body = ExpressionBuilder.Build(desc, instance, args, _resolver);

            if (body.Type == typeof(void) && returnType != typeof(void))
            {
                throw new InvalidOperationException($"成员返回 void，但委托期望返回 {returnType}。");
            }

            if (body.Type != returnType && returnType != typeof(void))
            {
                body = Expression.Convert(body, returnType);
            }

            var lambda = Expression.Lambda(delegateType, body, parameters);
            return lambda.Compile();
        }

        public static void ClearCache() => _cache.Clear();
    }
}
