#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.ReflectionCommon.Models
{
    public class ReflectionData
    {
        public string TypeName { get; set; } = string.Empty;

        public Dictionary<string, object?> Fields { get; set; } = new();

        public DateTime ExtractedAt { get; set; } = DateTime.Now;

        public bool IncludePrivate { get; set; }

        public int Depth { get; set; }

        public int SourceHash { get; set; }
    }

    public class TypeMetadata
    {
        public string FullName { get; set; } = string.Empty;

        public string FriendlyName { get; set; } = string.Empty;

        public string Namespace { get; set; } = string.Empty;

        public bool IsGenericType { get; set; }

        public bool IsAbstract { get; set; }

        public bool IsInterface { get; set; }

        public bool IsValueType { get; set; }

        public bool IsEnum { get; set; }

        public string? BaseType { get; set; }

        public List<string> Interfaces { get; set; } = new();

        public List<MemberMetadata> Properties { get; set; } = new();

        public List<MemberMetadata> Fields { get; set; } = new();

        public List<MethodMetadata> Methods { get; set; } = new();

        public List<ConstructorMetadata> Constructors { get; set; } = new();

        public List<string> Attributes { get; set; } = new();
    }

    public class MemberMetadata
    {
        public string Name { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public Type? Type { get; set; }

        public bool CanRead { get; set; }

        public bool CanWrite { get; set; }

        public bool IsStatic { get; set; }

        public string AccessLevel { get; set; } = "public";

        public List<string> Attributes { get; set; } = new();
    }

    public class MethodMetadata
    {
        public string Name { get; set; } = string.Empty;

        public string ReturnTypeName { get; set; } = string.Empty;

        public bool IsAsync { get; set; }

        public bool IsExtensionMethod { get; set; }

        public bool IsGenericMethod { get; set; }

        public List<ParameterMetadata> Parameters { get; set; } = new();

        public string AccessLevel { get; set; } = "public";

        public List<string> Attributes { get; set; } = new();
    }

    public class ParameterMetadata
    {
        public string Name { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public bool IsOptional { get; set; }

        public bool HasDefaultValue { get; set; }

        public string? DefaultValue { get; set; }

        public int Position { get; set; }
    }

    public class ConstructorMetadata
    {
        public List<ParameterMetadata> Parameters { get; set; } = new();

        public string AccessLevel { get; set; } = "public";

        public List<string> Attributes { get; set; } = new();
    }

    public class ReflectionResult
    {
        public bool Success { get; set; }

        public object? Value { get; set; }

        public string? ErrorMessage { get; set; }

        public long ElapsedMilliseconds { get; set; }

        public string OperationType { get; set; } = string.Empty;

        public static ReflectionResult Ok(object? value, string operationType = "")
        {
            return new ReflectionResult
            {
                Success = true,
                Value = value,
                OperationType = operationType
            };
        }

        public static ReflectionResult Fail(string errorMessage, string operationType = "")
        {
            return new ReflectionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                OperationType = operationType
            };
        }
    }

    public class ReflectionOptions
    {
        public bool IncludePrivate { get; set; } = true;

        public bool IncludeStatic { get; set; } = false;

        public bool IncludeInherited { get; set; } = true;

        public int MaxDepth { get; set; } = 3;

        public bool EnableCaching { get; set; } = true;

        public bool UseCompiledDelegates { get; set; } = true;

        public bool IgnoreExceptions { get; set; } = false;

        public System.Reflection.BindingFlags BindingFlags { get; set; } =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
    }
}
