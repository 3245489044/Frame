#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Modules.ReflectionCommon.Models;
using Modules.ReflectionMethod;

namespace Modules.ReflectionCommon
{
    public interface IReflectionService
    {
        #region 数据提取

        ReflectionData ExtractData(object target, ReflectionOptions? options = null);

        T? ExtractValue<T>(object target, string path);

        Dictionary<string, object?> ExtractByPattern(object target, string pattern);

        #endregion

        #region 动态调用

        ReflectionResult InvokeMethod(object target, string methodName, params object?[]? args);

        Task<ReflectionResult> InvokeMethodAsync(object target, string methodName, params object?[]? args);

        object? GetProperty(object target, string propertyName);
        void SetProperty(object target, string propertyName, object? value);

        object? GetField(object target, string fieldName);
        void SetField(object target, string fieldName, object? value);

        object? CreateInstance(Type type, params object?[]? args);

        #endregion

        #region 元数据

        TypeMetadata GetTypeMetadata(Type type);

        List<FunctionDescriptor> GetMethodDescriptors(Type type);

        #endregion
    }
}
