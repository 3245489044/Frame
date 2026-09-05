using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RoslynSpace.RoslynStringRefactoringFramework.Store
{
    #region 语法辅助工具（供内部重用）
    internal static class SyntaxHelper
    {
        public static bool TryExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel, Type targetType, out object value)
        {
            value = null;
            if (semanticModel.GetConstantValue(expr) is { HasValue: true } constant)
            {
                value = constant.Value;
                if (value == null) return true;
                if (targetType.IsAssignableFrom(value.GetType())) return true;
                try { value = Convert.ChangeType(value, targetType); return true; } catch { return false; }
            }
            if (expr is TypeOfExpressionSyntax typeofExpr)
            {
                var typeSymbol = semanticModel.GetTypeInfo(typeofExpr.Type).Type;
                if (typeSymbol != null)
                {
                    string assemblyQualifiedName = GetAssemblyQualifiedName(typeSymbol);
                    Type resolvedType = Type.GetType(assemblyQualifiedName, false);
                    if (resolvedType == null)
                        return false;
                    value = resolvedType;
                    return targetType.IsAssignableFrom(value.GetType());
                }
                return false;
            }
            if (expr is MemberAccessExpressionSyntax memberAccess && semanticModel.GetSymbolInfo(expr).Symbol is IFieldSymbol field && field.HasConstantValue)
            {
                value = field.ConstantValue;
                if (value == null) return true;
                if (targetType.IsAssignableFrom(value.GetType())) return true;
                try { value = Convert.ChangeType(value, targetType); return true; } catch { return false; }
            }
            if (expr is LiteralExpressionSyntax literal)
            {
                var token = literal.Token;
                object rawValue = token.Value;
                if (rawValue == null)
                {
                    value = null;
                    return true;
                }
                if (targetType.IsAssignableFrom(rawValue.GetType()))
                {
                    value = rawValue;
                    return true;
                }
                try
                {
                    value = Convert.ChangeType(rawValue, targetType);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public static string GetAssemblyQualifiedName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return null;
            string fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var assembly = typeSymbol.ContainingAssembly;
            if (assembly == null) return fullName;
            string assemblyDisplayName = assembly.Identity.GetDisplayName();
            return $"{fullName}, {assemblyDisplayName}";
        }

        public static ExpressionSyntax CreateLiteralExpression(object value) => value switch
        {
            null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
            Type t => SyntaxFactory.TypeOfExpression(GetTypeSyntax(t)),
            _ when value.GetType().IsEnum => SyntaxFactory.CastExpression(
                GetTypeSyntax(value.GetType()),
                CreateLiteralExpression(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())))),
            string s => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(s)),
            int i => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i)),
            bool b => SyntaxFactory.LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
            long l => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(l)),
            double d => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(d)),
            float f => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(f)),
            decimal m => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(m)),
            char c => SyntaxFactory.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SyntaxFactory.Literal(c)),
            _ => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value.ToString()))
        };

        public static TypeSyntax GetTypeSyntax(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var elementTypeSyntax = GetTypeSyntax(elementType);
                var rank = type.GetArrayRank();
                var rankSpecifiers = SyntaxFactory.List(
                    new[] { SyntaxFactory.ArrayRankSpecifier(
                SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    Enumerable.Repeat<ExpressionSyntax>(
                        SyntaxFactory.OmittedArraySizeExpression(), rank))) });
                return SyntaxFactory.ArrayType(elementTypeSyntax, rankSpecifiers);
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var typeArguments = type.GetGenericArguments();
                var typeArgumentSyntaxes = typeArguments.Select(GetTypeSyntax).ToArray();

                var typeName = GetTypeNameWithoutGenericArity(type);
                var genericName = SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(typeName),
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList(typeArgumentSyntaxes)));

                if (type.IsNested)
                {
                    var containingType = type.DeclaringType;
                    if (containingType != null)
                    {
                        var containingTypeSyntax = GetTypeSyntax(containingType);
                        return SyntaxFactory.QualifiedName(
                            (NameSyntax)containingTypeSyntax,
                            genericName);
                    }
                }
                return genericName;
            }

            var fullName = GetTypeFullName(type);
            return ParseTypeNameSafe(fullName);
        }

        private static string GetTypeNameWithoutGenericArity(Type type)
        {
            var name = type.Name;
            var backtickIndex = name.IndexOf('`');
            return backtickIndex > 0 ? name.Substring(0, backtickIndex) : name;
        }

        private static string GetTypeFullName(Type type)
        {
            if (type.IsNested)
            {
                var parentFullName = GetTypeFullName(type.DeclaringType);
                return parentFullName + "." + GetTypeNameWithoutGenericArity(type);
            }

            var ns = type.Namespace;
            var name = GetTypeNameWithoutGenericArity(type);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static TypeSyntax ParseTypeNameSafe(string fullName)
        {
            try
            {
                return SyntaxFactory.ParseTypeName(fullName);
            }
            catch
            {
                return SyntaxFactory.IdentifierName(fullName);
            }
        }
    }
    #endregion

    #region 通用静态元组存储模块（基于 StaticFieldStore 重构）
    public static class StaticTupleStore<TKey, TValue>
    {
        private const string DefaultFieldName = "Tuples";
        private const string DefaultClassName = "StaticTupleStore";

        private static readonly Func<ExpressionSyntax, SemanticModel, Dictionary<TKey, TValue>> DefaultDictionaryExtractor =
            (expr, model) =>
            {
                if (expr is ObjectCreationExpressionSyntax objCreation &&
                    objCreation.Initializer is InitializerExpressionSyntax initializer)
                {
                    var dict = new Dictionary<TKey, TValue>();
                    foreach (var element in initializer.Expressions)
                    {
                        if (element is InitializerExpressionSyntax kvInitializer && kvInitializer.Expressions.Count == 2)
                        {
                            var keyExpr = kvInitializer.Expressions[0];
                            var valueExpr = kvInitializer.Expressions[1];
                            if (SyntaxHelper.TryExtractConstantValue(keyExpr, model, typeof(TKey), out var keyObj) &&
                                SyntaxHelper.TryExtractConstantValue(valueExpr, model, typeof(TValue), out var valueObj))
                            {
                                dict.Add((TKey)keyObj, (TValue)valueObj);
                            }
                            else
                            {
                                throw new InvalidOperationException("无法提取键或值的常量值。");
                            }
                        }
                        else if (element is AssignmentExpressionSyntax assignment && assignment.Left is ImplicitElementAccessSyntax elementAccess)
                        {
                            var keyExpr = elementAccess.ArgumentList.Arguments.First().Expression;
                            var valueExpr = assignment.Right;
                            if (SyntaxHelper.TryExtractConstantValue(keyExpr, model, typeof(TKey), out var keyObj) &&
                                SyntaxHelper.TryExtractConstantValue(valueExpr, model, typeof(TValue), out var valueObj))
                            {
                                dict.Add((TKey)keyObj, (TValue)valueObj);
                            }
                            else
                            {
                                throw new InvalidOperationException("无法提取键或值的常量值。");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("无法识别的字典初始化元素。");
                        }
                    }
                    return dict;
                }
                throw new InvalidOperationException("无法识别的字典初始化表达式。");
            };

        private static readonly Func<Dictionary<TKey, TValue>, ExpressionSyntax> DefaultDictionaryGenerator =
            dict =>
            {
                var elements = new List<ExpressionSyntax>();
                foreach (var kv in dict)
                {
                    var keyExpr = SyntaxHelper.CreateLiteralExpression(kv.Key);
                    var valueExpr = SyntaxHelper.CreateLiteralExpression(kv.Value);
                    var kvInit = SyntaxFactory.InitializerExpression(
                        SyntaxKind.ComplexElementInitializerExpression,
                        SyntaxFactory.SeparatedList(new ExpressionSyntax[] { keyExpr, valueExpr }));
                    elements.Add(kvInit);
                }
                var init = SyntaxFactory.InitializerExpression(
                    SyntaxKind.CollectionInitializerExpression,
                    SyntaxFactory.SeparatedList(elements));

                var dictType = typeof(Dictionary<TKey, TValue>);
                var typeSyntax = SyntaxHelper.GetTypeSyntax(dictType);
                return SyntaxFactory.ObjectCreationExpression(
                    typeSyntax,
                    SyntaxFactory.ArgumentList(),
                    init);
            };

        private static Dictionary<TKey, TValue> GetDictionary(string code, string className, string fieldName)
        {
            var dict = StaticFieldStore<Dictionary<TKey, TValue>>.GetOrCreateValue(
                code,
                defaultValue: null,
                fieldName,
                className,
                extractor: DefaultDictionaryExtractor,
                generator: null);
            return dict ?? new Dictionary<TKey, TValue>();
        }

        private static string SetDictionary(string code, Dictionary<TKey, TValue> dict, string className, string fieldName)
        {
            return StaticFieldStore<Dictionary<TKey, TValue>>.SetValue(
                code,
                dict,
                fieldName,
                className,
                DefaultDictionaryGenerator);
        }

        public static IReadOnlyDictionary<TKey, TValue> ParseTuples(string code, string fieldName = DefaultFieldName)
            => ParseTuples(code, null, fieldName);

        public static IReadOnlyDictionary<TKey, TValue> ParseTuples(string code, string fieldName,
            Func<ExpressionSyntax, SemanticModel, TKey> keyExtractor,
            Func<ExpressionSyntax, SemanticModel, TValue> valueExtractor)
        {
            throw new NotSupportedException("自定义提取器重载当前不支持，请使用无提取器重载。");
        }

        public static IReadOnlyDictionary<TKey, TValue> ParseTuples(string code, string className, string fieldName = DefaultFieldName)
        {
            var dict = GetDictionary(code, className, fieldName);
            return dict;
        }

        public static IReadOnlyDictionary<TKey, TValue> ParseTuples(string code, string className, string fieldName,
            Func<ExpressionSyntax, SemanticModel, TKey> keyExtractor,
            Func<ExpressionSyntax, SemanticModel, TValue> valueExtractor)
        {
            throw new NotSupportedException("自定义提取器重载当前不支持，请使用无提取器重载。");
        }

        public static TValue FindValueByKey(string code, TKey key, string fieldName = DefaultFieldName)
            => FindValueByKey(code, key, null, fieldName);

        public static TValue FindValueByKey(string code, TKey key, string fieldName,
            Func<ExpressionSyntax, SemanticModel, TKey> keyExtractor,
            Func<ExpressionSyntax, SemanticModel, TValue> valueExtractor)
        {
            throw new NotSupportedException("自定义提取器重载当前不支持，请使用无提取器重载。");
        }

        public static TValue FindValueByKey(string code, TKey key, string className, string fieldName = DefaultFieldName)
        {
            var dict = GetDictionary(code, className, fieldName);
            return dict.TryGetValue(key, out var value) ? value : default;
        }

        public static TValue FindValueByKey(string code, TKey key, string className, string fieldName,
            Func<ExpressionSyntax, SemanticModel, TKey> keyExtractor,
            Func<ExpressionSyntax, SemanticModel, TValue> valueExtractor)
        {
            throw new NotSupportedException("自定义提取器重载当前不支持，请使用无提取器重载。");
        }

        public static string AddTuple(string code, TKey key, TValue value, string fieldName = DefaultFieldName)
            => AddTuple(code, key, value, null, fieldName);

        public static string AddTuple(string code, TKey key, TValue value, string fieldName,
            Func<TKey, ExpressionSyntax> keyGenerator, Func<TValue, ExpressionSyntax> valueGenerator)
        {
            throw new NotSupportedException("自定义生成器重载当前不支持，请使用无生成器重载。");
        }

        public static string AddTuple(string code, TKey key, TValue value, string className, string fieldName = DefaultFieldName)
        {
            var dict = GetDictionary(code, className, fieldName);
            dict[key] = value;
            return SetDictionary(code, dict, className ?? DefaultClassName, fieldName);
        }

        public static string AddTuple(string code, TKey key, TValue value, string className, string fieldName,
            Func<TKey, ExpressionSyntax> keyGenerator, Func<TValue, ExpressionSyntax> valueGenerator)
        {
            throw new NotSupportedException("自定义生成器重载当前不支持，请使用无生成器重载。");
        }

        public static string AddRange(string code, IEnumerable<KeyValuePair<TKey, TValue>> pairs, string className = null, string fieldName = DefaultFieldName)
        {
            var dict = GetDictionary(code, className, fieldName);
            foreach (var kv in pairs)
                dict[kv.Key] = kv.Value;
            return SetDictionary(code, dict, className ?? DefaultClassName, fieldName);
        }

        public static string RemoveKey(string code, TKey key, string className = null, string fieldName = DefaultFieldName)
        {
            var dict = GetDictionary(code, className, fieldName);
            dict.Remove(key);
            return SetDictionary(code, dict, className ?? DefaultClassName, fieldName);
        }
    }

    public static class StringTupleStore
    {
        private const string DefaultFieldName = "Tuples";
        private const string DefaultClassName = "StringTupleStore";

        public static IReadOnlyDictionary<string, string> ParseTuples(string code, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.ParseTuples(code, null, fieldName);

        public static IReadOnlyDictionary<string, string> ParseTuples(string code, string className, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.ParseTuples(code, className, fieldName);

        public static string FindValueByKey(string code, string key, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.FindValueByKey(code, key, null, fieldName);

        public static string FindValueByKey(string code, string key, string className, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.FindValueByKey(code, key, className, fieldName);

        public static string AddTuple(string code, string key, string value, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.AddTuple(code, key, value, null, fieldName);

        public static string AddTuple(string code, string key, string value, string className, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.AddTuple(code, key, value, className, fieldName);

        public static string AddRange(string code, IEnumerable<KeyValuePair<string, string>> pairs, string className = null, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.AddRange(code, pairs, className, fieldName);

        public static string RemoveKey(string code, string key, string className = null, string fieldName = DefaultFieldName)
            => StaticTupleStore<string, string>.RemoveKey(code, key, className, fieldName);
    }

    public static class StaticTupleStore
    {
        public static IReadOnlyDictionary<string, string> ParseTuples(string code, string fieldName = "Tuples")
            => StringTupleStore.ParseTuples(code, fieldName);

        public static string FindValueByKey(string code, string key, string fieldName = "Tuples")
            => StringTupleStore.FindValueByKey(code, key, fieldName);

        public static string AddTuple(string code, string key, string value, string fieldName = "Tuples")
            => StringTupleStore.AddTuple(code, key, value, fieldName);

        public static IReadOnlyDictionary<string, string> ParseTuples(string code, string className, string fieldName = "Tuples")
            => StringTupleStore.ParseTuples(code, className, fieldName);

        public static string FindValueByKey(string code, string key, string className, string fieldName = "Tuples")
            => StringTupleStore.FindValueByKey(code, key, className, fieldName);

        public static string AddTuple(string code, string key, string value, string className, string fieldName = "Tuples")
            => StringTupleStore.AddTuple(code, key, value, className, fieldName);
    }
    #endregion

    #region 通用静态字段存储模块 - 支持任意类型
    public static class StaticFieldStore<T>
    {
        private const string DefaultFieldName = "Value";
        private const string DefaultClassName = "StaticStore";

        public static readonly Func<ExpressionSyntax, SemanticModel, T> DefaultExtractor = (expr, model) =>
            SyntaxHelper.TryExtractConstantValue(expr, model, typeof(T), out var v) ? (T)v : throw new InvalidOperationException($"无法提取 {typeof(T)} 常量值。");

        public static readonly Func<T, ExpressionSyntax> DefaultGenerator = v => SyntaxHelper.CreateLiteralExpression(v);

        private static bool TryInitializeContext(string code, out AnalysisContext context)
        {
            try
            {
                context = new AnalysisContext(code);
                return true;
            }
            catch
            {
                context = null;
                return false;
            }
        }

        private static (ClassDeclarationSyntax Class, FieldDeclarationSyntax Field) FindField(AnalysisContext context, string className, string fieldName)
        {
            if (string.IsNullOrEmpty(className))
            {
                foreach (var cls in context.GetClasses("", includeNested: false))
                {
                    var fields = context.GetFields(cls.Identifier.ValueText, fieldName);
                    if (fields.Any())
                        return (cls, fields.First());
                }
                return (null, null);
            }

            var targetClass = context.GetClasses(className, includeNested: false).FirstOrDefault();
            if (targetClass == null)
                return (null, null);

            var fieldDecl = context.GetFields(className, fieldName).FirstOrDefault();
            return (targetClass, fieldDecl);
        }

        public static T GetOrCreateValue(string code, T defaultValue = default, string fieldName = DefaultFieldName, string className = null,
            Func<ExpressionSyntax, SemanticModel, T> extractor = null, Func<T, ExpressionSyntax> generator = null)
        {
            if (!TryInitializeContext(code, out var context))
                return defaultValue;

            var (targetClass, fieldDecl) = FindField(context, className, fieldName);
            if (fieldDecl == null)
                return defaultValue;

            extractor ??= DefaultExtractor;
            return extractor(fieldDecl.Declaration.Variables.First().Initializer.Value, context.SemanticModel);
        }

        public static string SetValue(string code, T value, string fieldName = DefaultFieldName, string className = null,
            Func<T, ExpressionSyntax> generator = null)
        {
            if (!TryInitializeContext(code, out var context))
                return code;

            var (targetClass, fieldDecl) = FindField(context, className, fieldName);
            if (fieldDecl == null)
            {
                generator ??= DefaultGenerator;
                var newField = SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ParseTypeName(typeof(T).Name),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(fieldName)
                                .WithInitializer(SyntaxFactory.EqualsValueClause(generator(value))))))
                    .WithModifiers(SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                        SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

                var root = context.Root;
                var classNode = context.GetClasses(className, includeNested: false).FirstOrDefault();
                if (classNode == null)
                    return code;

                var newClass = classNode.AddMembers(newField);
                root = root.ReplaceNode(classNode, newClass);
                return root.NormalizeWhitespace().ToFullString();
            }

            generator ??= DefaultGenerator;
            var newValue = generator(value);
            var variable = fieldDecl.Declaration.Variables.First();
            var newVariable = variable.WithInitializer(SyntaxFactory.EqualsValueClause(newValue));
            var newFieldDecl = fieldDecl.ReplaceNode(variable, newVariable);

            var newRoot = context.Root.ReplaceNode(fieldDecl, newFieldDecl);
            return newRoot.NormalizeWhitespace().ToFullString();
        }
    }
    #endregion
}
