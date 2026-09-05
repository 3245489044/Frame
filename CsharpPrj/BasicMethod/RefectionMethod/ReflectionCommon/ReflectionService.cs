#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Modules.ReflectionCommon.Caching;
using Modules.ReflectionCommon.Models;
using Modules.ReflectionMethod;
using Modules.ReflectionMethod.Dynamic;

namespace Modules.ReflectionCommon
{
    public class ReflectionService : IReflectionService
    {
        private readonly ReflectionDataExtractor _extractor;
        private readonly DynamicMethodExecutor _executor;
        private readonly MethodResolver _resolver;
        private readonly Cache<Type, TypeMetadata> _metadataCache;
        private readonly ReflectionOptions _defaultOptions;

        public ReflectionService() : this(new ReflectionOptions()) { }

        public ReflectionService(ReflectionOptions options)
        {
            _extractor = new ReflectionDataExtractor();
            _executor = new DynamicMethodExecutor();
            _resolver = new MethodResolver();
            _metadataCache = new Cache<Type, TypeMetadata>();
            _defaultOptions = options ?? new ReflectionOptions();
        }

        #region 数据提取

        public ReflectionData ExtractData(object target, ReflectionOptions? options = null)
        {
            _ = target ?? throw new ArgumentNullException(nameof(target));

            var opts = options ?? _defaultOptions;
            var sw = Stopwatch.StartNew();

            var fields = _extractor.ExtractAllFields(target, opts.IncludePrivate);

            sw.Stop();

            return new ReflectionData
            {
                TypeName = target.GetType().FullName ?? target.GetType().Name,
                Fields = fields,
                ExtractedAt = DateTime.Now,
                IncludePrivate = opts.IncludePrivate,
                Depth = opts.MaxDepth,
                SourceHash = target.GetHashCode()
            };
        }

        public T? ExtractValue<T>(object target, string path)
        {
            return _extractor.ExtractTypedValue<T>(target, path);
        }

        public Dictionary<string, object?> ExtractByPattern(object target, string pattern)
        {
            return _extractor.ExtractByPattern(target, pattern);
        }

        #endregion

        #region 动态调用

        public ReflectionResult InvokeMethod(object target, string methodName, params object?[]? args)
        {
            return ExecuteWithTiming("InvokeMethod", () =>
            {
                var type = target.GetType();
                var match = _resolver.Resolve(type, methodName, args ?? Array.Empty<object>());
                return _executor.Execute(match.Method, target, match.ConvertedArguments);
            });
        }

        public async Task<ReflectionResult> InvokeMethodAsync(object target, string methodName, params object?[]? args)
        {
            return await ExecuteWithTimingAsync("InvokeMethodAsync", async () =>
            {
                var type = target.GetType();
                var match = _resolver.Resolve(type, methodName, args ?? Array.Empty<object>());
                var rawResult = _executor.Execute(match.Method, target, match.ConvertedArguments);
                return await AsyncTaskUnwrapper.UnwrapAsync(rawResult);
            });
        }

        public object? GetProperty(object target, string propertyName)
        {
            if (target.TryGetPropertyValue(propertyName, out var value))
                return value;
            return null;
        }

        public void SetProperty(object target, string propertyName, object? value)
        {
            target.TrySetPropertyValue(propertyName, value);
        }

        public object? GetField(object target, string fieldName)
        {
            return _extractor.GetFieldValue<object>(target, fieldName);
        }

        public void SetField(object target, string fieldName, object? value)
        {
            target.TrySetFieldValue(fieldName, value);
        }

        public object? CreateInstance(Type type, params object?[]? args)
        {
            return Activator.CreateInstance(type, args);
        }

        #endregion

        #region 元数据

        public TypeMetadata GetTypeMetadata(Type type)
        {
            _ = type ?? throw new ArgumentNullException(nameof(type));

            return _metadataCache.GetOrAdd(type, t =>
            {
                var flags = _defaultOptions.BindingFlags;

                var metadata = new TypeMetadata
                {
                    FullName = t.FullName ?? t.Name,
                    FriendlyName = t.GetFriendlyName(),
                    Namespace = t.Namespace ?? string.Empty,
                    IsGenericType = t.IsGenericType,
                    IsAbstract = t.IsAbstract,
                    IsInterface = t.IsInterface,
                    IsValueType = t.IsValueType,
                    IsEnum = t.IsEnum,
                    BaseType = t.BaseType?.FullName,
                    Interfaces = t.GetInterfaces().Select(i => i.FullName ?? i.Name).ToList(),
                    Attributes = t.GetCustomAttributes(true).Select(a => a.GetType().Name).ToList()
                };

                foreach (var prop in t.GetProperties(flags))
                {
                    metadata.Properties.Add(new MemberMetadata
                    {
                        Name = prop.Name,
                        TypeName = prop.PropertyType.GetFriendlyName(),
                        Type = prop.PropertyType,
                        CanRead = prop.CanRead,
                        CanWrite = prop.CanWrite,
                        IsStatic = prop.GetAccessors(true).Any(a => a.IsStatic),
                        AccessLevel = GetAccessLevel(prop),
                        Attributes = prop.GetCustomAttributes(true).Select(a => a.GetType().Name).ToList()
                    });
                }

                foreach (var field in t.GetFields(flags))
                {
                    metadata.Fields.Add(new MemberMetadata
                    {
                        Name = field.Name,
                        TypeName = field.FieldType.GetFriendlyName(),
                        Type = field.FieldType,
                        CanRead = true,
                        CanWrite = !field.IsInitOnly,
                        IsStatic = field.IsStatic,
                        AccessLevel = GetAccessLevel(field),
                        Attributes = field.GetCustomAttributes(true).Select(a => a.GetType().Name).ToList()
                    });
                }

                foreach (var method in t.GetMethods(flags | BindingFlags.Static))
                {
                    if (method.IsSpecialName)
                        continue;

                    metadata.Methods.Add(new MethodMetadata
                    {
                        Name = method.Name,
                        ReturnTypeName = method.ReturnType.GetFriendlyName(),
                        IsAsync = method.ReturnType.IsAsyncType(),
                        IsGenericMethod = method.IsGenericMethod,
                        Parameters = BuildParameterMetadata(method.GetParameters()),
                        AccessLevel = GetAccessLevel(method),
                        Attributes = method.GetCustomAttributes(true).Select(a => a.GetType().Name).ToList()
                    });
                }

                foreach (var ctor in t.GetConstructors(flags))
                {
                    metadata.Constructors.Add(new ConstructorMetadata
                    {
                        Parameters = BuildParameterMetadata(ctor.GetParameters()),
                        AccessLevel = GetAccessLevel(ctor),
                        Attributes = ctor.GetCustomAttributes(true).Select(a => a.GetType().Name).ToList()
                    });
                }

                return metadata;
            });
        }

        public List<FunctionDescriptor> GetMethodDescriptors(Type type)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
                .Where(m => !m.IsSpecialName);

            var descriptors = new List<FunctionDescriptor>();
            foreach (var method in methods)
            {
                var descriptor = new FunctionDescriptor
                {
                    Name = method.Name,
                    ReturnType = method.ReturnType,
                    IsAsync = method.ReturnType.IsAsyncType(),
                    Parameters = method.GetParameters().Select(p => new ParameterDescriptor
                    {
                        Name = p.Name ?? "param",
                        Type = p.ParameterType,
                        IsOptional = p.IsOptional,
                        DefaultValue = p.DefaultValue
                    }).ToList()
                };
                descriptors.Add(descriptor);
            }
            return descriptors;
        }

        #endregion

        #region 辅助方法

        private static ReflectionResult ExecuteWithTiming(string operationType, Func<object?> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = action();
                sw.Stop();
                var reflectionResult = ReflectionResult.Ok(result, operationType);
                reflectionResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                return reflectionResult;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var reflectionResult = ReflectionResult.Fail(ex.Message, operationType);
                reflectionResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                return reflectionResult;
            }
        }

        private static async Task<ReflectionResult> ExecuteWithTimingAsync(string operationType, Func<Task<object?>> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await action();
                sw.Stop();
                var reflectionResult = ReflectionResult.Ok(result, operationType);
                reflectionResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                return reflectionResult;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var reflectionResult = ReflectionResult.Fail(ex.Message, operationType);
                reflectionResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                return reflectionResult;
            }
        }


        private static List<ParameterMetadata> BuildParameterMetadata(ParameterInfo[] parameters)
        {
            return parameters.Select(p => new ParameterMetadata
            {
                Name = p.Name ?? "param",
                TypeName = p.ParameterType.GetFriendlyName(),
                IsOptional = p.IsOptional,
                HasDefaultValue = p.HasDefaultValue,
                DefaultValue = p.DefaultValue?.ToString(),
                Position = p.Position
            }).ToList();
        }

        private static string GetAccessLevel(MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => GetAccessLevelFromFlags(field.IsPublic, field.IsFamily, field.IsAssembly, field.IsPrivate),
                PropertyInfo prop => GetPropertyAccessLevel(prop),
                MethodInfo method => GetAccessLevelFromFlags(method.IsPublic, method.IsFamily, method.IsAssembly, method.IsPrivate),
                ConstructorInfo ctor => GetAccessLevelFromFlags(ctor.IsPublic, ctor.IsFamily, ctor.IsAssembly, ctor.IsPrivate),
                _ => "internal"
            };
        }

        private static string GetAccessLevelFromFlags(bool isPublic, bool isFamily, bool isAssembly, bool isPrivate)
        {
            if (isPublic) return "public";
            if (isFamily) return "protected";
            if (isAssembly) return "internal";
            if (isPrivate) return "private";
            return "internal";
        }

        private static string GetPropertyAccessLevel(PropertyInfo property)
        {
            var getter = property.GetGetMethod(true);
            var setter = property.GetSetMethod(true);

            if (getter != null)
            {
                if (getter.IsPublic) return "public";
                if (getter.IsFamily) return "protected";
                if (getter.IsAssembly) return "internal";
            }
            if (setter != null)
            {
                if (setter.IsPublic) return "public";
                if (setter.IsFamily) return "protected";
                if (setter.IsAssembly) return "internal";
            }

            return "private";
        }

        #endregion
    }
}
