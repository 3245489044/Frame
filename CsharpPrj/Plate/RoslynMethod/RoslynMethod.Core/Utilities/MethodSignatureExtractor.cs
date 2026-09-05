using System.Reflection;
using System.Text;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace RoslynSpace.RoslynSyntaxUtilities.Core
{
    public static class MethodSignatureExtractor
    {
        #region 模型类

        public class MethodSignature
        {
            public string Name { get; set; }
            public string AccessModifier { get; set; }
            public bool IsStatic { get; set; }
            public bool IsAbstract { get; set; }
            public bool IsVirtual { get; set; }
            public bool IsSealed { get; set; }
            public bool IsGeneric { get; set; }
            public Type ReturnType { get; set; }
            public List<ParameterInfo> Parameters { get; set; }
            public string FullSignature { get; set; }
            public MethodInfo MethodInfo { get; set; }
        }

        public class ParameterInfo
        {
            public string Name { get; set; }
            public Type Type { get; set; }
            public bool IsOut { get; set; }
            public bool IsRef { get; set; }
            public bool HasDefaultValue { get; set; }
            public object DefaultValue { get; set; }
        }

        public class FieldSignature
        {
            public string Name { get; set; }
            public string AccessModifier { get; set; }
            public bool IsStatic { get; set; }
            public bool IsReadOnly { get; set; }
            public bool IsConstant { get; set; }
            public Type FieldType { get; set; }
            public string FullSignature { get; set; }
            public FieldInfo FieldInfo { get; set; }
        }

        public class MethodConversionInfo
        {
            public MethodSignature OriginalSignature { get; set; }
            public bool CanConvert { get; set; }
            public bool IsSuccessful { get; set; }
            public Func<object, object> ConvertedFunc { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class FieldConversionInfo
        {
            public FieldSignature OriginalSignature { get; set; }
            public bool CanConvert { get; set; }
            public bool IsSuccessful { get; set; }
            public Func<object, object> ConvertedFunc { get; set; }
        }

        public class ConversionResult
        {
            public List<MethodConversionInfo> MethodResults { get; set; } = new List<MethodConversionInfo>();
            public List<FieldConversionInfo> FieldResults { get; set; } = new List<FieldConversionInfo>();
            public List<Func<object, object>> SuccessfulConversions { get; set; } = new List<Func<object, object>>();

            public int TotalCount => MethodResults.Count + FieldResults.Count;
            public int SuccessfulCount => SuccessfulConversions.Count;
            public int FailedCount => TotalCount - SuccessfulCount;
        }

        #endregion

        #region 方法签名提取

        public static List<MethodSignature> ExtractMethodSignatures(
            Assembly assembly = null,
            Type targetType = null,
            Func<MethodSignature, bool> filter = null)
        {
            ValidateArguments(assembly, targetType);
            var typesToScan = GetTypesToScan(assembly, targetType);
            var results = new List<MethodSignature>();

            foreach (var type in typesToScan)
            {
                foreach (var method in type.GetMethods(GetBindingFlags()))
                {
                    var signature = CreateMethodSignature(method);
                    if (ShouldIncludeSignature(signature, filter))
                    {
                        results.Add(signature);
                    }
                }
            }

            return results;
        }

        private static void ValidateArguments(Assembly assembly, Type targetType)
        {
            if (assembly == null && targetType == null)
                throw new ArgumentException("必须提供assembly或targetType参数");
        }

        private static IEnumerable<Type> GetTypesToScan(Assembly assembly, Type targetType)
        {
            return targetType != null
                ? new[] { targetType }
                : assembly.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic);
        }

        private static BindingFlags GetBindingFlags()
        {
            return BindingFlags.Public | BindingFlags.NonPublic |
                   BindingFlags.Static | BindingFlags.Instance |
                   BindingFlags.DeclaredOnly;
        }

        private static bool ShouldIncludeSignature(MethodSignature signature, Func<MethodSignature, bool> filter)
        {
            return filter?.Invoke(signature) ?? IsDefaultTargetSignature(signature);
        }

        private static bool IsDefaultTargetSignature(MethodSignature signature)
        {
            return signature.AccessModifier == "public" &&
                   (signature.ReturnType == typeof(Func<object, object>) ||
                    (signature.ReturnType.IsGenericType &&
                     signature.ReturnType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                     signature.ReturnType.GetGenericArguments().Length == 2));
        }

        private static MethodSignature CreateMethodSignature(MethodInfo methodInfo)
        {
            var parameters = methodInfo.GetParameters()
                                     .Select(CreateParameterInfo)
                                     .ToList();

            return new MethodSignature
            {
                Name = methodInfo.Name,
                AccessModifier = GetAccessModifier(methodInfo),
                IsStatic = methodInfo.IsStatic,
                IsAbstract = methodInfo.IsAbstract,
                IsVirtual = methodInfo.IsVirtual,
                IsSealed = methodInfo.IsFinal,
                IsGeneric = methodInfo.IsGenericMethod,
                ReturnType = methodInfo.ReturnType,
                Parameters = parameters,
                FullSignature = GenerateFullSignature(methodInfo, parameters),
                MethodInfo = methodInfo
            };
        }

        private static ParameterInfo CreateParameterInfo(System.Reflection.ParameterInfo parameter)
        {
            return new ParameterInfo
            {
                Name = parameter.Name,
                Type = parameter.ParameterType,
                IsOut = parameter.IsOut,
                IsRef = parameter.ParameterType.IsByRef && !parameter.IsOut,
                HasDefaultValue = parameter.HasDefaultValue,
                DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null
            };
        }

        private static string GetAccessModifier(MethodInfo method)
            => AccessibilityConverter.GetAccessibilityString(method);

        private static string GenerateFullSignature(MethodInfo method, List<ParameterInfo> parameters)
        {
            var accessModifier = GetAccessModifier(method);
            var staticKeyword = method.IsStatic ? "static " : "";
            var returnType = TypeDisplayHelper.GetTypeDisplayName(method.ReturnType);
            var paramString = string.Join(", ", parameters.Select(p =>
                $"{TypeDisplayHelper.GetTypeDisplayName(p.Type)} {p.Name}"));

            return $"{accessModifier} {staticKeyword}{returnType} {method.Name}({paramString})";
        }

        #endregion

        #region 字段签名提取

        public static List<FieldSignature> ExtractFieldSignatures(
            Assembly assembly = null,
            Type targetType = null,
            Func<FieldSignature, bool> filter = null)
        {
            ValidateArguments(assembly, targetType);
            var typesToScan = GetTypesToScan(assembly, targetType);
            var results = new List<FieldSignature>();

            foreach (var type in typesToScan)
            {
                foreach (var field in type.GetFields(GetBindingFlags()))
                {
                    var signature = CreateFieldSignature(field);
                    if (ShouldIncludeFieldSignature(signature, filter))
                    {
                        results.Add(signature);
                    }
                }
            }

            return results;
        }

        private static bool ShouldIncludeFieldSignature(FieldSignature signature, Func<FieldSignature, bool> filter)
        {
            return filter?.Invoke(signature) ?? IsTargetFieldSignature(signature);
        }

        private static bool IsTargetFieldSignature(FieldSignature signature)
        {
            return signature.FieldType == typeof(Func<object, object>) ||
                   (signature.FieldType.IsGenericType &&
                    signature.FieldType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                    signature.FieldType.GetGenericArguments().Length == 2);
        }

        private static FieldSignature CreateFieldSignature(FieldInfo fieldInfo)
        {
            return new FieldSignature
            {
                Name = fieldInfo.Name,
                AccessModifier = GetFieldAccessModifier(fieldInfo),
                IsStatic = fieldInfo.IsStatic,
                IsReadOnly = fieldInfo.IsInitOnly,
                IsConstant = fieldInfo.IsLiteral,
                FieldType = fieldInfo.FieldType,
                FullSignature = GenerateFieldFullSignature(fieldInfo),
                FieldInfo = fieldInfo
            };
        }

        private static string GetFieldAccessModifier(FieldInfo field)
        {
            if (field.IsPublic) return "public";
            if (field.IsPrivate) return "private";
            if (field.IsFamily) return "protected";
            if (field.IsAssembly) return "internal";
            if (field.IsFamilyOrAssembly) return "protected internal";
            return "private";
        }

        private static string GenerateFieldFullSignature(FieldInfo field)
        {
            var accessModifier = GetFieldAccessModifier(field);
            var staticKeyword = field.IsStatic ? "static " : "";
            var readonlyKeyword = field.IsInitOnly ? "readonly " : "";
            var fieldType = TypeDisplayHelper.GetTypeDisplayName(field.FieldType);

            return $"{accessModifier} {staticKeyword}{readonlyKeyword}{fieldType} {field.Name}";
        }

        #endregion

        #region 方法签名转Func转换器

        public static class MethodSignatureToFuncConverter
        {
            public static List<Func<object, object>> ConvertToFuncList(List<MethodSignature> signatures)
            {
                Func<MethodSignature, Func<object, object>> converter = ConvertToFunc;
                return signatures.Select(converter)
                               .Where(func => func != null)
                               .ToList();
            }

            public static void PopulateDictionary(List<MethodSignature> signatures, Dictionary<string, object> targetDictionary)
            {
                if (signatures == null || targetDictionary == null)
                    return;

                Func<MethodSignature, Func<object, object>> converter = ConvertToFunc;

                var methodPairs = signatures
                    .Where(sig => !string.IsNullOrEmpty(sig?.Name))
                    .Select(sig => new { Name = sig.Name, Func = converter(sig) })
                    .Where(pair => pair.Func != null);

                foreach (var pair in methodPairs)
                {
                    targetDictionary[pair.Name] = pair.Func;
                }
            }

            public static Func<object, object> ConvertToFunc(MethodSignature signature)
            {
                if (signature == null) return null;

                try
                {
                    if (!IsConvertibleToFunc(signature)) return null;

                    return signature.IsStatic
                        ? CreateStaticMethodFunc(signature)
                        : CreateInstanceMethodFunc(signature);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"转换方法 {signature.Name} 时出错: {ex.Message}");
                    return null;
                }
            }

            public static Func<object, object> ConvertToFunc(MethodSignature signature, object targetInstance)
            {
                if (signature == null || targetInstance == null) return null;
                if (signature.IsStatic) return CreateStaticMethodFunc(signature);

                try
                {
                    return CreateInstanceMethodFuncWithTarget(signature, targetInstance);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"转换实例方法 {signature.Name} 时出错: {ex.Message}");
                    return null;
                }
            }

            private static bool IsConvertibleToFunc(MethodSignature signature)
            {
                if (signature.Parameters.Count != 1)
                {
                    Console.WriteLine($"方法 {signature.Name} 参数数量不符合要求: 期望1个参数，实际{signature.Parameters.Count}个");
                    return false;
                }

                if (signature.ReturnType == typeof(void))
                {
                    Console.WriteLine($"方法 {signature.Name} 返回类型为 void，无法转换为 Func<object, object>");
                    return false;
                }

                return true;
            }

            private static Func<object, object> CreateStaticMethodFunc(MethodSignature signature)
            {
                var methodInfo = signature.MethodInfo;
                var parameterType = signature.Parameters[0].Type;

                return input =>
                {
                    try
                    {
                        var convertedInput = TypeDisplayHelper.ConvertToType(input, parameterType);
                        return methodInfo.Invoke(null, new[] { convertedInput });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"调用静态方法 {signature.Name} 时出错: {ex.Message}");
                        return null;
                    }
                };
            }

            private static Func<object, object> CreateInstanceMethodFunc(MethodSignature signature)
            {
                return input =>
                {
                    Console.WriteLine($"实例方法 {signature.Name} 需要目标对象实例，请使用 ConvertToFunc(signature, targetInstance) 重载");
                    return null;
                };
            }

            private static Func<object, object> CreateInstanceMethodFuncWithTarget(MethodSignature signature, object targetInstance)
            {
                var methodInfo = signature.MethodInfo;
                var parameterType = signature.Parameters[0].Type;

                return input =>
                {
                    try
                    {
                        var convertedInput = TypeDisplayHelper.ConvertToType(input, parameterType);
                        return methodInfo.Invoke(targetInstance, new[] { convertedInput });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"调用实例方法 {signature.Name} 时出错: {ex.Message}");
                        return null;
                    }
                };
            }

            public static ConversionResult ConvertWithDetails(List<MethodSignature> signatures, object targetInstance = null)
            {
                var result = new ConversionResult();

                foreach (var signature in signatures)
                {
                    var conversionInfo = new MethodConversionInfo
                    {
                        OriginalSignature = signature,
                        CanConvert = IsConvertibleToFunc(signature)
                    };

                    if (conversionInfo.CanConvert)
                    {
                        conversionInfo.ConvertedFunc = targetInstance != null && !signature.IsStatic
                            ? ConvertToFunc(signature, targetInstance)
                            : ConvertToFunc(signature);

                        conversionInfo.IsSuccessful = conversionInfo.ConvertedFunc != null;
                    }

                    result.MethodResults.Add(conversionInfo);
                }

                result.SuccessfulConversions = result.MethodResults
                    .Where(r => r.IsSuccessful)
                    .Select(r => r.ConvertedFunc)
                    .ToList();

                return result;
            }
        }

        #endregion

        #region 字段签名转Func转换器

        public static class FieldSignatureToFuncConverter
        {
            public static void PopulateDictionaryWithDetails(List<FieldSignature> signatures, Dictionary<string, object> targetDictionary, object targetInstance = null)
            {
                if (signatures == null || targetDictionary == null) return;

                var conversionResult = ConvertWithDetails(signatures, targetInstance);

                foreach (var result in conversionResult.FieldResults)
                {
                    if (result.IsSuccessful && result.ConvertedFunc != null)
                    {
                        targetDictionary[result.OriginalSignature.Name] = result.ConvertedFunc;
                        Console.WriteLine($"成功存入字段: {result.OriginalSignature.Name}");
                    }
                    else
                    {
                        var status = result.CanConvert ? "转换失败" : "无法转换";
                        Console.WriteLine($"字段 {result.OriginalSignature.Name} {status}");
                    }
                }
            }

            public static List<Func<object, object>> ConvertToFuncList(List<FieldSignature> signatures)
            {
                Func<FieldSignature, Func<object, object>> converter = ConvertToFunc;
                return signatures.Select(converter)
                               .Where(func => func != null)
                               .ToList();
            }

            public static void PopulateDictionary(List<FieldSignature> signatures, Dictionary<string, object> targetDictionary)
            {
                if (signatures == null || targetDictionary == null) return;

                Func<FieldSignature, Func<object, object>> converter = ConvertToFunc;

                var fieldPairs = signatures
                    .Where(sig => !string.IsNullOrEmpty(sig?.Name))
                    .Select(sig => new { Name = sig.Name, Func = converter(sig) })
                    .Where(pair => pair.Func != null);

                foreach (var pair in fieldPairs)
                {
                    targetDictionary[pair.Name] = pair.Func;
                }
            }

            public static Func<object, object> ConvertToFunc(FieldSignature signature)
            {
                if (signature == null) return null;

                try
                {
                    if (!IsConvertibleToFunc(signature)) return null;

                    return signature.IsStatic
                        ? CreateStaticFieldFunc(signature)
                        : CreateInstanceFieldFunc(signature);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"转换字段 {signature.Name} 时出错: {ex.Message}");
                    return null;
                }
            }

            public static Func<object, object> ConvertToFunc(FieldSignature signature, object targetInstance)
            {
                if (signature == null || targetInstance == null) return null;
                if (signature.IsStatic) return CreateStaticFieldFunc(signature);

                try
                {
                    return CreateInstanceFieldFuncWithTarget(signature, targetInstance);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"转换实例字段 {signature.Name} 时出错: {ex.Message}");
                    return null;
                }
            }

            private static bool IsConvertibleToFunc(FieldSignature signature)
            {
                if (signature.FieldType == typeof(Func<object, object>))
                    return true;

                if (signature.FieldType.IsGenericType &&
                    signature.FieldType.GetGenericTypeDefinition() == typeof(Func<,>))
                {
                    var genericArgs = signature.FieldType.GetGenericArguments();
                    if (genericArgs.Length == 2)
                    {
                        Console.WriteLine($"字段 {signature.Name} 类型为 {signature.FieldType.Name}，需要包装转换");
                        return true;
                    }
                }

                Console.WriteLine($"字段 {signature.Name} 类型 {signature.FieldType.Name} 无法转换为 Func<object, object>");
                return false;
            }

            private static Func<object, object> CreateStaticFieldFunc(FieldSignature signature)
            {
                var fieldInfo = signature.FieldInfo;

                return input =>
                {
                    try
                    {
                        var fieldValue = fieldInfo.GetValue(null);
                        return fieldValue is Func<object, object> directFunc
                            ? directFunc(input)
                            : WrapDelegate(fieldValue, input);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"访问静态字段 {signature.Name} 时出错: {ex.Message}");
                        return null;
                    }
                };
            }

            private static Func<object, object> CreateInstanceFieldFunc(FieldSignature signature)
            {
                return input =>
                {
                    Console.WriteLine($"实例字段 {signature.Name} 需要目标对象实例，请使用 ConvertToFunc(signature, targetInstance) 重载");
                    return null;
                };
            }

            private static Func<object, object> CreateInstanceFieldFuncWithTarget(FieldSignature signature, object targetInstance)
            {
                var fieldInfo = signature.FieldInfo;

                return input =>
                {
                    try
                    {
                        var fieldValue = fieldInfo.GetValue(targetInstance);
                        return fieldValue is Func<object, object> directFunc
                            ? directFunc(input)
                            : WrapDelegate(fieldValue, input);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"访问实例字段 {signature.Name} 时出错: {ex.Message}");
                        return null;
                    }
                };
            }

            private static object WrapDelegate(object delegateInstance, object input)
            {
                if (delegateInstance == null) return null;

                var delegateType = delegateInstance.GetType();

                if (delegateType == typeof(Func<object, object>))
                    return ((Func<object, object>)delegateInstance)(input);

                if (delegateType.IsGenericType &&
                    delegateType.GetGenericTypeDefinition() == typeof(Func<,>))
                {
                    var method = delegateType.GetMethod("Invoke");
                    var parameters = method.GetParameters();

                    if (parameters.Length == 1)
                    {
                        try
                        {
                            var convertedInput = TypeDisplayHelper.ConvertToType(input, parameters[0].ParameterType);
                            var result = method.Invoke(delegateInstance, new[] { convertedInput });
                            return result;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"调用委托时出错: {ex.Message}");
                            return null;
                        }
                    }
                }

                Console.WriteLine($"不支持的委托类型: {delegateType.Name}");
                return null;
            }

            public static ConversionResult ConvertWithDetails(List<FieldSignature> signatures, object targetInstance = null)
            {
                var result = new ConversionResult();

                foreach (var signature in signatures)
                {
                    var conversionInfo = new FieldConversionInfo
                    {
                        OriginalSignature = signature,
                        CanConvert = IsConvertibleToFunc(signature)
                    };

                    if (conversionInfo.CanConvert)
                    {
                        conversionInfo.ConvertedFunc = targetInstance != null && !signature.IsStatic
                            ? ConvertToFunc(signature, targetInstance)
                            : ConvertToFunc(signature);

                        conversionInfo.IsSuccessful = conversionInfo.ConvertedFunc != null;
                    }

                    result.FieldResults.Add(conversionInfo);
                }

                result.SuccessfulConversions = result.FieldResults
                    .Where(r => r.IsSuccessful)
                    .Select(r => r.ConvertedFunc)
                    .ToList();

                return result;
            }
        }

        #endregion

        #region 辅助工具类

        [Obsolete("请使用 Core.Utilities.TypeDisplayHelper 替代")]
        internal static class TypeDisplayHelper
        {
            private static readonly Dictionary<Type, string> TypeAliases = new Dictionary<Type, string>
            {
                { typeof(object), "object" },
                { typeof(string), "string" },
                { typeof(int), "int" },
                { typeof(long), "long" },
                { typeof(bool), "bool" },
                { typeof(double), "double" },
                { typeof(float), "float" },
                { typeof(decimal), "decimal" },
                { typeof(Func<object, object>), "Func<object, object>" }
            };

            public static string GetTypeDisplayName(Type type)
            {
                if (type == null) return "void";
                if (TypeAliases.TryGetValue(type, out string alias)) return alias;
                if (!type.IsGenericType) return type.Name.Split('`')[0];

                var genericArgs = type.GetGenericArguments();
                var baseName = type.GetGenericTypeDefinition().Name.Split('`')[0];
                return $"{baseName}<{string.Join(", ", genericArgs.Select(GetTypeDisplayName))}>";
            }

            public static string FormatDefaultValue(object defaultValue)
            {
                if (defaultValue == null) return "null";
                if (defaultValue is string) return $"\"{defaultValue}\"";
                if (defaultValue is bool) return defaultValue.ToString().ToLower();
                return defaultValue.ToString();
            }

            public static object ConvertToType(object input, Type targetType)
            {
                if (input == null)
                    return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

                if (targetType == typeof(object) || targetType.IsInstanceOfType(input))
                    return input;

                try
                {
                    if (targetType.IsEnum && input is string enumString)
                        return Enum.Parse(targetType, enumString);

                    return Convert.ChangeType(input, targetType);
                }
                catch
                {
                    Console.WriteLine($"无法将类型 {input.GetType().Name} 转换为 {targetType.Name}");
                    return input;
                }
            }
        }

        #endregion

        #region 方法签名打印

        public static class MethodSignaturePrinter
        {
            public static string GenerateMethodSignaturesString(List<MethodSignature> signatures, string title = "方法签名列表")
            {
                if (signatures == null || signatures.Count == 0)
                    return "未找到匹配的方法签名。";

                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 80));
                sb.AppendLine($" {title} (共 {signatures.Count} 个方法)");
                sb.AppendLine(new string('=', 80));

                for (int i = 0; i < signatures.Count; i++)
                {
                    sb.Append(GenerateMethodSignatureString(signatures[i], i + 1));
                    if (i < signatures.Count - 1)
                        sb.AppendLine(new string('-', 80));
                }

                return sb.ToString();
            }

            private static string GenerateMethodSignatureString(MethodSignature signature, int index)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{index}] {signature.FullSignature}");
                sb.AppendLine($"    方法名称: {signature.Name}");
                sb.AppendLine($"    访问修饰符: {signature.AccessModifier}");
                sb.AppendLine($"    返回类型: {TypeDisplayHelper.GetTypeDisplayName(signature.ReturnType)}");

                var modifiers = new List<string>();
                if (signature.IsStatic) modifiers.Add("static");
                if (signature.IsAbstract) modifiers.Add("abstract");
                if (signature.IsVirtual) modifiers.Add("virtual");
                if (signature.IsSealed) modifiers.Add("sealed");
                if (signature.IsGeneric) modifiers.Add("generic");

                if (modifiers.Count > 0)
                    sb.AppendLine($"    修饰符: {string.Join(", ", modifiers)}");

                if (signature.Parameters.Count > 0)
                {
                    sb.AppendLine("    参数列表:");
                    foreach (var param in signature.Parameters)
                    {
                        var paramModifiers = new List<string>();
                        if (param.IsOut) paramModifiers.Add("out");
                        if (param.IsRef) paramModifiers.Add("ref");

                        var modifierStr = paramModifiers.Count > 0 ? $"{string.Join(" ", paramModifiers)} " : "";
                        var defaultValueStr = param.HasDefaultValue ? $" = {TypeDisplayHelper.FormatDefaultValue(param.DefaultValue)}" : "";

                        sb.AppendLine($"      {modifierStr}{TypeDisplayHelper.GetTypeDisplayName(param.Type)} {param.Name}{defaultValueStr}");
                    }
                }
                else
                {
                    sb.AppendLine("    参数列表: 无参数");
                }

                return sb.ToString();
            }
        }

        #endregion
    }
}
