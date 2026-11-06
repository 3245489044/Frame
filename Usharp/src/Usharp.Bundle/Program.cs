using System;
using System.IO;
using Usharp.Adapters.Declarations;
using Usharp.Bundle;

// 声明单点编译工具（G1）：<configRoot> <outDir> → manifest / round-spec / reference-schema / keyspace / commands。
// UI 声明（专用闭包）：ui <configRoot> <outFile> → ui-declaration.generated.json
//                     uiregions <configRoot> <outFile> → ui-regions.json（兼容视图，带生成标记）
// 唯一编译点 = DeclaredBundleBuilder；本工具只做参数解析与产出上报。
// selfcheck：值映射反例自检（E-4；零测试工程，退出码 0/1 供回归套件断言）。
if (args.Length >= 3 && string.Equals(args[0], "keyspace-facts", StringComparison.OrdinalIgnoreCase))
{
    var factsConfigRoot = args[1];
    var factsOutFile = args[2];
    try
    {
        var artifacts = DeclaredBundleBuilder.Build(factsConfigRoot);
        var normalized = KeySpaceFactsNormalizer.Normalize(artifacts.KeySpaceJson);
        File.WriteAllText(factsOutFile, normalized);
        Console.WriteLine($"keyspace-facts: errors={artifacts.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"keyspace-facts 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "keyspace-facts-json", StringComparison.OrdinalIgnoreCase))
{
    var factsJsonIn = args[1];
    var factsJsonOut = args[2];
    try
    {
        if (!File.Exists(factsJsonIn))
        {
            Console.Error.WriteLine($"keyspace-facts-json 输入缺失: {factsJsonIn}");
            return 2;
        }

        var normalized = KeySpaceFactsNormalizer.Normalize(File.ReadAllText(factsJsonIn));
        File.WriteAllText(factsJsonOut, normalized);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"keyspace-facts-json 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 1 && string.Equals(args[0], "selfcheck", StringComparison.OrdinalIgnoreCase))
{
    var issues = UsharpValueMapperSelfCheck.Run();
    if (issues.Count == 0)
    {
        Console.WriteLine("value-mapper-selfcheck: ok");
        return 0;
    }

    foreach (var issue in issues) Console.Error.WriteLine(issue);
    return 1;
}

if (args.Length >= 3 && string.Equals(args[0], "uiregions", StringComparison.OrdinalIgnoreCase))
{
    var regionsConfigRoot = args[1];
    var regionsOutFile = args[2];
    try
    {
        var regions = DeclaredBundleBuilder.BuildUiRegions(regionsConfigRoot);
        if (regions.Json is null)
        {
            Console.Error.WriteLine($"ui-regions 不可产出：errors={regions.Errors}");
            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(regionsOutFile, regions.Json);
        Console.WriteLine($"ui-regions: errors={regions.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ui-regions 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "ui", StringComparison.OrdinalIgnoreCase))
{
    var uiConfigRoot = args[1];
    var uiOutFile = args[2];
    try
    {
        var ui = DeclaredBundleBuilder.BuildUiDeclaration(uiConfigRoot);
        if (ui.Json is null)
        {
            Console.Error.WriteLine($"ui-declaration 不可产出：errors={ui.Errors}");
            foreach (var diagnostic in ui.Diagnostics)
            {
                if (diagnostic.Severity != Usharp.Semantics.UsharpSeverity.Error) continue;
                Console.Error.WriteLine("  " + diagnostic);
            }

            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(uiOutFile, ui.Json);
        Console.WriteLine($"ui-declaration: fingerprint={ui.Fingerprint} errors={ui.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ui-declaration 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "gatherviews", StringComparison.OrdinalIgnoreCase))
{
    var gatherConfigRoot = args[1];
    var gatherOutFile = args[2];
    try
    {
        var emitted = DeclaredBundleBuilder.BuildGatherViews(gatherConfigRoot);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"gather-views 不可产出：errors={emitted.Errors}");
            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(gatherOutFile, emitted.Json);
        Console.WriteLine($"gather-views: errors={emitted.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"gather-views 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "fields", StringComparison.OrdinalIgnoreCase))
{
    var fieldsConfigRoot = args[1];
    var fieldsOutFile = args[2];
    try
    {
        var emitted = DeclaredBundleBuilder.BuildFieldVocabulary(fieldsConfigRoot);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"field-vocabulary 不可产出：errors={emitted.Errors}");
            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(fieldsOutFile, emitted.Json);
        Console.WriteLine($"field-vocabulary: errors={emitted.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"field-vocabulary 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "uiprogram", StringComparison.OrdinalIgnoreCase))
{
    var programConfigRoot = args[1];
    var programOutFile = args[2];
    try
    {
        var emitted = DeclaredBundleBuilder.BuildUiProgram(programConfigRoot);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"ui-program 不可产出：errors={emitted.Errors}");
            foreach (var diagnostic in emitted.Diagnostics)
            {
                if (diagnostic.Severity != Usharp.Semantics.UsharpSeverity.Error) continue;
                Console.Error.WriteLine("  " + diagnostic);
            }

            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(programOutFile, emitted.Json);
        Console.WriteLine($"ui-program: errors={emitted.Errors} fingerprint={emitted.Fingerprint}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ui-program 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length >= 3 && string.Equals(args[0], "surfacemap", StringComparison.OrdinalIgnoreCase))
{
    var mapConfigRoot = args[1];
    var mapOutFile = args[2];
    try
    {
        var emitted = DeclaredBundleBuilder.BuildSurfaceMap(mapConfigRoot);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"surface-map 不可产出：errors={emitted.Errors}");
            return 1;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(mapOutFile, emitted.Json);
        Console.WriteLine($"surface-map: errors={emitted.Errors}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"surface-map 失败: {ex.Message}");
        return 2;
    }
}

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: Usharp.Bundle <configRoot> <outDir>");
    return 2;
}

var configRoot = args[0];
var outDir = args[1];

try
{
    var layout = Usharp.Semantics.DeclarationLayout.FromRoot(configRoot, out var layoutDiagnostics);
    foreach (var diagnostic in layoutDiagnostics) Console.Error.WriteLine("  layout: " + diagnostic);

    var artifacts = DeclaredBundleBuilder.Build(configRoot, layout);
    DeclaredBundleBuilder.WriteTo(outDir, artifacts);

    Console.WriteLine(
        $"declared-bundle: files={artifacts.Files.Count} errors={artifacts.Errors} fingerprint={artifacts.SourceFingerprint}");
    Console.WriteLine($"  out={Path.GetFullPath(outDir)}");

    // 诊断事实可见（AX.NoSilentOutlet）：error 明细逐条上报，不静默只报计数。
    foreach (var diagnostic in artifacts.Diagnostics)
    {
        if (diagnostic.Severity != Usharp.Semantics.UsharpSeverity.Error) continue;
        Console.Error.WriteLine("  " + diagnostic);
    }

    // 生成 = 机械操作：声明编译 error 不判生成失败（产物流已落盘，事实可读）；
    // 失败姿态由消费侧声明门（IDeclarationGate，G3）裁决，本工具不自持策略。
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"declared-bundle 失败: {ex.Message}");
    return 2;
}
