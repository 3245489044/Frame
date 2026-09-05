
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Modules.ComponentData.Reflection
{
    public static class ExpressionBuilder
    {
        private static readonly IMemberResolver _defaultResolver = new DefaultMemberResolver();

        public static Expression Build(InvocationDescriptor desc, Expression? instance, Expression[] arguments, IMemberResolver? resolver = null)
        {
            resolver ??= _defaultResolver;
            switch (desc.MemberKind)
            {
                case MemberKind.Method:
                    return BuildMethodCall(desc, instance, arguments, resolver);
                case MemberKind.Property:
                    return BuildPropertyAccess(desc, instance, arguments, resolver);
                case MemberKind.Field:
                    return BuildFieldAccess(desc, instance, arguments, resolver);
                case MemberKind.Constructor:
                    return BuildConstructorCall(desc, arguments, resolver);
                default:
                    throw new NotSupportedException($"不支持的成员类型: {desc.MemberKind}");
            }
        }

        #region 辅助方法：类型转换

        private static Expression? EnsureInstanceType(Expression? instance, MemberInfo member)
        {
            if (instance == null) return null;
            var declaringType = member.DeclaringType;
            if (declaringType != null && !declaringType.IsAssignableFrom(instance.Type))
            {
                return Expression.Convert(instance, declaringType);
            }
            return instance;
        }

        private static Expression[] ConvertArguments(Expression[] args, ParameterInfo[] parameters)
        {
            if (args.Length != parameters.Length)
                throw new ArgumentException($"参数数量不匹配：期望 {parameters.Length}，实际 {args.Length}");

            var converted = new Expression[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var paramType = parameters[i].ParameterType;

                if (paramType.IsByRef)
                {
                    if (arg.Type != paramType)
                        throw new ArgumentException($"参数 {i} 是引用参数，表达式类型必须为 {paramType}，实际为 {arg.Type}。");
                    converted[i] = arg;
                }
                else
                {
                    if (arg.Type != paramType && !paramType.IsAssignableFrom(arg.Type))
                    {
                        converted[i] = Expression.Convert(arg, paramType);
                    }
                    else
                    {
                        converted[i] = arg;
                    }
                }
            }
            return converted;
        }

        #endregion

        #region 具体构建方法

        private static MethodCallExpression BuildMethodCall(InvocationDescriptor desc, Expression? instance, Expression[] args, IMemberResolver resolver)
        {
            var method = resolver.ResolveMethod(desc);
            instance = EnsureInstanceType(instance, method);
            var parameters = method.GetParameters();
            var convertedArgs = ConvertArguments(args, parameters);
            return Expression.Call(instance, method, convertedArgs);
        }

        private static Expression BuildPropertyAccess(InvocationDescriptor desc, Expression? instance, Expression[] args, IMemberResolver resolver)
        {
            var prop = resolver.ResolveProperty(desc);
            instance = EnsureInstanceType(instance, prop);

            if (desc.IsSetter)
            {
                if (args.Length == 0)
                    throw new ArgumentException("属性赋值至少需要一个值参数。");
                var valueArg = args.Last();
                var indexArgs = args.Take(args.Length - 1).ToArray();

                var indexParams = prop.GetIndexParameters();
                if (indexArgs.Length != indexParams.Length)
                    throw new ArgumentException("索引参数数量不匹配。");
                var convertedIndexArgs = ConvertArguments(indexArgs, indexParams);

                Expression propAccess;
                if (convertedIndexArgs.Length == 0)
                    propAccess = Expression.Property(instance, prop);
                else
                    propAccess = Expression.Property(instance, prop, convertedIndexArgs);

                var valueParamType = prop.PropertyType;
                Expression convertedValue = valueArg;
                if (valueArg.Type != valueParamType && !valueParamType.IsAssignableFrom(valueArg.Type))
                    convertedValue = Expression.Convert(valueArg, valueParamType);

                return Expression.Assign(propAccess, convertedValue);
            }
            else
            {
                var indexParams = prop.GetIndexParameters();
                if (args.Length != indexParams.Length)
                    throw new ArgumentException("索引参数数量不匹配。");
                var convertedIndexArgs = ConvertArguments(args, indexParams);

                if (convertedIndexArgs.Length == 0)
                    return Expression.Property(instance, prop);
                else
                    return Expression.Property(instance, prop, convertedIndexArgs);
            }
        }

        private static Expression BuildFieldAccess(InvocationDescriptor desc, Expression? instance, Expression[] args, IMemberResolver resolver)
        {
            var field = resolver.ResolveField(desc);
            instance = EnsureInstanceType(instance, field);

            if (desc.IsSetter)
            {
                if (args.Length != 1)
                    throw new ArgumentException("字段赋值需要一个值参数。");
                var valueArg = args[0];
                var fieldType = field.FieldType;
                Expression convertedValue = valueArg;
                if (valueArg.Type != fieldType && !fieldType.IsAssignableFrom(valueArg.Type))
                    convertedValue = Expression.Convert(valueArg, fieldType);
                var fieldAccess = Expression.Field(instance, field);
                return Expression.Assign(fieldAccess, convertedValue);
            }
            else
            {
                return Expression.Field(instance, field);
            }
        }

        private static NewExpression BuildConstructorCall(InvocationDescriptor desc, Expression[] args, IMemberResolver resolver)
        {
            var ctor = resolver.ResolveConstructor(desc);
            var parameters = ctor.GetParameters();
            var convertedArgs = ConvertArguments(args, parameters);
            return Expression.New(ctor, convertedArgs);
        }

        #endregion
    }
}
