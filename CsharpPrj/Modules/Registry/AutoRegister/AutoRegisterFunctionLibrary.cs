using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TConfig;

namespace Modules.Registry.AutoRegister;

/// <summary>
/// 通用数据驱动函数库注册器：清单只声明资源与程序集，本类负责读取 AutoRegister 配置、
/// 解析程序集、执行反射注册并按 expected 清单做启动期自检。
/// <para>
/// 产品/资源层只需提供 <c>FunctionRegistrations/&lt;Lib&gt;/registration.json</c> 与 expected 清单，
/// 不再需要为每个库编写 <c>MathFunctionRegistrar</c> 之类的路径探测代码。
/// </para>
/// </summary>
[FunctionLibrary]
public static class AutoRegisterFunctionLibrary
{
    public static void RegisterAll(ITrConfigRegistry registry, IFunctionLibraryContext context)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);

        var configRelative = context.Descriptor.ConfigPath;
        if (string.IsNullOrWhiteSpace(configRelative))
            throw new FunctionLibraryLoadException(
                $"函数库 '{context.Descriptor.Name}' 使用 AutoRegisterFunctionLibrary，但清单未声明 config。");

        var configPath = context.ResolveRequiredResource(configRelative);
        context.Info($"config={configPath}");

        var engine = new AutoRegisterEngine(
            new AutoRegisterService(),
            resolveAssembly: context.ResolveAssembly);

        var results = engine.ProcessConfig(configPath, registry);
        var success = results.Sum(r => r.Result.SuccessCount);
        var failed = results.Sum(r => r.Result.FailCount);

        if (failed > 0)
        {
            var details = string.Join("; ", results
                .Where(r => r.Result.FailCount > 0)
                .SelectMany(r => r.Result.RegisteredItems
                    .Where(i => i.Status != RegisteredItemStatus.Success)
                    .Select(i => $"{i.ClassName}.{i.MethodName}: {i.ErrorMessage}")));
            throw new FunctionLibraryLoadException(
                $"函数库 '{context.Descriptor.Name}' 注册失败 {failed} 项。{details}");
        }

        AssertExpected(registry, context);

        context.Info($"registered={success}, failed={failed}, config={configPath}");
    }

    private static void AssertExpected(ITrConfigRegistry registry, IFunctionLibraryContext context)
    {
        var expectedRelative = context.Descriptor.ExpectedPath;
        if (string.IsNullOrWhiteSpace(expectedRelative))
            return;

        var expectedPath = context.ResolveOptionalResource(expectedRelative);
        if (expectedPath is null)
            throw new FileNotFoundException(
                $"函数库 '{context.Descriptor.Name}' 声明的 expected 清单不存在：{expectedRelative}");

        var expected = LoadExpected(expectedPath);
        if (expected.Count == 0)
            return;

        var missing = expected.Where(name => !registry.Contains(name)).ToList();
        if (missing.Count == 0)
            return;

        throw new FunctionLibraryLoadException(
            $"函数库 '{context.Descriptor.Name}' 启动自检失败：期望 {expected.Count} 个函数，" +
            $"实际缺失 {missing.Count} 个：[{string.Join(", ", missing)}]。expected={expectedPath}");
    }

    private static IReadOnlyList<string> LoadExpected(string expectedPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(expectedPath));
        if (!doc.RootElement.TryGetProperty("expected", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList();
    }
}
