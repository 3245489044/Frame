using System.Reflection;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace RoslynSpace.RoslynSyntaxUtilities.Analysis
{
    public static class AssemblyAnalyzer
    {
        #region 模型类

        public class AssemblyMethodAnalysis
        {
            public string AssemblyName { get; set; }
            public string AssemblyFullName { get; set; }
            public string AssemblyLocation { get; set; }
            public DateTime AnalysisTime { get; set; } = DateTime.Now;
            public List<TypeMethodInfo> Types { get; } = new List<TypeMethodInfo>();

            public int TotalMethodCount => Types.Sum(t => t.Methods.Count);
            public int TotalTypeCount => Types.Count;
            public int PublicMethodCount => Types.Sum(t => t.Methods.Count(m => m.AccessModifier == "public"));
            public int PrivateMethodCount => Types.Sum(t => t.Methods.Count(m => m.AccessModifier == "private"));
            public int StaticMethodCount => Types.Sum(t => t.Methods.Count(m => m.IsStatic));
            public int VirtualMethodCount => Types.Sum(t => t.Methods.Count(m => m.IsVirtual));
        }

        public class TypeMethodInfo
        {
            public string TypeName { get; set; }
            public string FullTypeName { get; set; }
            public string Namespace { get; set; }
            public bool IsStatic { get; set; }
            public bool IsAbstract { get; set; }
            public bool IsSealed { get; set; }
            public bool IsInterface { get; set; }
            public List<MethodSignature> Methods { get; } = new List<MethodSignature>();
            public int MethodCount => Methods.Count;
        }

        public class MethodSignature
        {
            public string Name { get; set; }
            public string AccessModifier { get; set; }
            public bool IsStatic { get; set; }
            public bool IsAbstract { get; set; }
            public bool IsVirtual { get; set; }
            public bool IsSealed { get; set; }
            public bool IsGeneric { get; set; }
            public string ReturnType { get; set; }
            public List<ParameterInfo> Parameters { get; } = new List<ParameterInfo>();
            public string FullSignature { get; set; }
        }

        public class ParameterInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public bool IsOut { get; set; }
            public bool IsRef { get; set; }
            public bool HasDefaultValue { get; set; }
            public object DefaultValue { get; set; }
        }

        #endregion

        public static AssemblyMethodAnalysis AnalyzeMethods(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var analysis = new AssemblyMethodAnalysis
            {
                AssemblyName = assembly.GetName().Name,
                AssemblyFullName = assembly.FullName,
                AssemblyLocation = assembly.Location
            };

            analysis.Types.AddRange(
                assembly.GetTypes()
                    .Where(t => !t.IsSpecialName)
                    .Select(CreateTypeMethodInfo)
            );

            return analysis;
        }

        private static TypeMethodInfo CreateTypeMethodInfo(Type type)
        {
            var typeInfo = new TypeMethodInfo
            {
                TypeName = type.Name,
                FullTypeName = type.FullName,
                Namespace = type.Namespace,
                IsStatic = type.IsAbstract && type.IsSealed,
                IsAbstract = type.IsAbstract,
                IsSealed = type.IsSealed,
                IsInterface = type.IsInterface
            };

            typeInfo.Methods.AddRange(
                type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                              BindingFlags.Instance | BindingFlags.Static |
                              BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(CreateMethodSignature)
            );

            return typeInfo;
        }

        private static MethodSignature CreateMethodSignature(MethodInfo method)
        {
            var parameters = method.GetParameters()
                .Select(p => new ParameterInfo
                {
                    Name = p.Name,
                    Type = GetTypeName(p.ParameterType),
                    IsOut = p.IsOut,
                    IsRef = p.ParameterType.IsByRef && !p.IsOut,
                    HasDefaultValue = p.HasDefaultValue,
                    DefaultValue = p.HasDefaultValue ? p.DefaultValue : null
                })
                .ToList();

            var signature = new MethodSignature
            {
                Name = method.Name,
                AccessModifier = GetAccessModifier(method),
                IsStatic = method.IsStatic,
                IsAbstract = method.IsAbstract,
                IsVirtual = method.IsVirtual && !method.IsFinal,
                IsSealed = method.IsFinal && method.IsVirtual,
                IsGeneric = method.IsGenericMethod,
                ReturnType = GetTypeName(method.ReturnType),
                FullSignature = BuildFullSignature(method, parameters)
            };

            signature.Parameters.AddRange(parameters);

            return signature;
        }

        private static string BuildFullSignature(MethodInfo method, List<ParameterInfo> parameters)
        {
            var modifiers = new List<string>();

            string accessModifier = GetAccessModifier(method);
            if (!string.IsNullOrEmpty(accessModifier))
                modifiers.Add(accessModifier);

            if (method.IsStatic) modifiers.Add("static");
            if (method.IsAbstract) modifiers.Add("abstract");
            if (method.IsVirtual && !method.IsFinal) modifiers.Add("virtual");
            if (method.IsFinal && method.IsVirtual) modifiers.Add("sealed");

            string returnType = GetTypeName(method.ReturnType);

            string methodName = method.Name;
            if (method.IsGenericMethod)
            {
                var genericArgs = string.Join(", ",
                    method.GetGenericArguments().Select(GetTypeName));
                methodName += $"<{genericArgs}>";
            }

            string parameterList = string.Join(", ", parameters.Select(FormatParameter));

            return $"{string.Join(" ", modifiers)} {returnType} {methodName}({parameterList})".Trim();
        }

        private static string FormatParameter(ParameterInfo parameter)
        {
            var direction = parameter.IsOut ? "out " :
                           parameter.IsRef ? "ref " : "";

            return $"{direction}{parameter.Type} {parameter.Name}";
        }

        private static string GetAccessModifier(MethodInfo method)
            => AccessibilityConverter.GetAccessibilityString(method);

        private static string GetTypeName(Type type)
        {
            if (type.IsByRef)
                type = type.GetElementType();

            if (type.IsArray)
            {
                var elementType = GetTypeName(type.GetElementType());
                var rank = type.GetArrayRank();
                var brackets = rank == 1 ? "[]" : $"[{new string(',', rank - 1)}]";
                return elementType + brackets;
            }

            if (type.IsGenericType)
            {
                var name = type.Name.Split('`')[0];
                var genericArgs = string.Join(", ",
                    type.GetGenericArguments().Select(GetTypeName));
                return $"{name}<{genericArgs}>";
            }

            return type.Name;
        }
    }
}
