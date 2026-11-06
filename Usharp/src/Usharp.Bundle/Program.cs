using System;
using System.IO;
using Share.HostAssembly.Commands;
using Usharp.Adapters.Declarations;
using Usharp.Bundle;

// 声明单点编译工具（G1）：<configRoot> <outDir> → manifest / round-spec / reference-schema / keyspace / commands。
// UI 声明（专用闭包）：ui <configRoot> <outFile> → ui-declaration.generated.json
//                     uiregions <configRoot> <outFile> → ui-regions.json（兼容视图，带生成标记）
// 计算清单：compute <configRoot> <outFile> → compute.manifest.json（仅含 [Compute] 的单元/程序/attest 面）
// 唯一编译点 = DeclaredBundleBuilder；本工具只做参数解析与产出上报。
// selfcheck：值映射反例自检（E-4；零测试工程，退出码 0/1 供回归套件断言）。
// 子命令路由 / 位置参数个数 / 退出码 / 异常边界 = 控制台命令宿主（Share.HostAssembly.Commands.ConsoleCommandHost）单实现。
const string Usage = "用法: Usharp.Bundle <configRoot> <outDir>";

var host = new ConsoleCommandHost(Usage)
    .Register(new CommandSpec("keyspace-facts", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var artifacts = DeclaredBundleBuilder.Build(rest[0]);
        var normalized = KeySpaceFactsNormalizer.Normalize(artifacts.KeySpaceJson);
        File.WriteAllText(rest[1], normalized);
        output.WriteLine($"keyspace-facts: errors={artifacts.Errors}");
        return ConsoleCommandHost.ExitOk;
    }))
    .Register(new CommandSpec("keyspace-facts-json", "<inFile> <outFile>", 2, (rest, output) =>
    {
        if (!File.Exists(rest[0]))
        {
            Console.Error.WriteLine($"keyspace-facts-json 输入缺失: {rest[0]}");
            return ConsoleCommandHost.ExitUsage;
        }

        var normalized = KeySpaceFactsNormalizer.Normalize(File.ReadAllText(rest[0]));
        File.WriteAllText(rest[1], normalized);
        return ConsoleCommandHost.ExitOk;
    }))
    .Register(new CommandSpec("selfcheck", "[-]", 0, (_, output) =>
    {
        var issues = UsharpValueMapperSelfCheck.Run();
        if (issues.Count == 0)
        {
            output.WriteLine("value-mapper-selfcheck: ok");
            return ConsoleCommandHost.ExitOk;
        }

        foreach (var issue in issues) Console.Error.WriteLine(issue);
        return ConsoleCommandHost.ExitFailed;
    }))
    .Register(new CommandSpec("uiregions", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var regions = DeclaredBundleBuilder.BuildUiRegions(rest[0]);
        if (regions.Json is null)
        {
            Console.Error.WriteLine($"ui-regions 不可产出：errors={regions.Errors}");
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], regions.Json);
        output.WriteLine($"ui-regions: errors={regions.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "ui-regions" })
    .Register(new CommandSpec("ui", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var ui = DeclaredBundleBuilder.BuildUiDeclaration(rest[0]);
        if (ui.Json is null)
        {
            Console.Error.WriteLine($"ui-declaration 不可产出：errors={ui.Errors}");
            WriteErrorDiagnostics(ui.Diagnostics);
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], ui.Json);
        output.WriteLine($"ui-declaration: fingerprint={ui.Fingerprint} errors={ui.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "ui-declaration" })
    .Register(new CommandSpec("gatherviews", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildGatherViews(rest[0]);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"gather-views 不可产出：errors={emitted.Errors}");
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], emitted.Json);
        output.WriteLine($"gather-views: errors={emitted.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "gather-views" })
    .Register(new CommandSpec("fields", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildFieldVocabulary(rest[0]);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"field-vocabulary 不可产出：errors={emitted.Errors}");
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], emitted.Json);
        output.WriteLine($"field-vocabulary: errors={emitted.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "field-vocabulary" })
    .Register(new CommandSpec("uiprogram", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildUiProgram(rest[0]);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"ui-program 不可产出：errors={emitted.Errors}");
            WriteErrorDiagnostics(emitted.Diagnostics);
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], emitted.Json);
        output.WriteLine($"ui-program: errors={emitted.Errors} fingerprint={emitted.Fingerprint}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "ui-program" })
    .Register(new CommandSpec("surface", "<configRoot> <outFile> [tokensFile]", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildSurfaceProgram(rest[0], rest.Count > 2 ? rest[2] : null);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"surface-program 不可产出：errors={emitted.Errors}");
            WriteErrorDiagnostics(emitted.Diagnostics);
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], emitted.Json);
        output.WriteLine($"surface-program: errors={emitted.Errors} fingerprint={emitted.Fingerprint}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "surface-program" })
    .Register(new CommandSpec("surfacemap", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildSurfaceMap(rest[0]);
        if (emitted.Json is null)
        {
            Console.Error.WriteLine($"surface-map 不可产出：errors={emitted.Errors}");
            return ConsoleCommandHost.ExitFailed;
        }

        DeclaredBundleBuilder.WriteUiDeclaration(rest[1], emitted.Json);
        output.WriteLine($"surface-map: errors={emitted.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "surface-map" })
    .Register(new CommandSpec("compute", "<configRoot> <outFile>", 2, (rest, output) =>
    {
        var emitted = DeclaredBundleBuilder.BuildComputeManifest(rest[0]);
        if (emitted.Errors > 0)
        {
            foreach (var diagnostic in emitted.Diagnostics) Console.Error.WriteLine("  " + diagnostic);
            Console.Error.WriteLine($"compute-manifest 不可产出：errors={emitted.Errors}");
            return ConsoleCommandHost.ExitFailed;
        }

        File.WriteAllText(rest[1], emitted.Json);
        output.WriteLine($"compute-manifest: units={emitted.Units} errors={emitted.Errors}");
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "compute-manifest" })
    // 无子命令：<configRoot> <outDir> 位置参数模式（既有调用面，参数名口径不变）。
    .WithFallback(new CommandSpec("declared-bundle", "<configRoot> <outDir>", 2, (rest, output) =>
    {
        var layout = Usharp.Semantics.DeclarationLayout.FromRoot(rest[0], out var layoutDiagnostics);
        foreach (var diagnostic in layoutDiagnostics) Console.Error.WriteLine("  layout: " + diagnostic);

        var artifacts = DeclaredBundleBuilder.Build(rest[0], layout);
        DeclaredBundleBuilder.WriteTo(rest[1], artifacts);

        output.WriteLine(
            $"declared-bundle: files={artifacts.Files.Count} errors={artifacts.Errors} fingerprint={artifacts.SourceFingerprint}");
        output.WriteLine($"  out={Path.GetFullPath(rest[1])}");

        // 诊断事实可见（AX.NoSilentOutlet）：error 明细逐条上报，不静默只报计数。
        WriteErrorDiagnostics(artifacts.Diagnostics);

        // 生成 = 机械操作：声明编译 error 不判生成失败（产物流已落盘，事实可读）；
        // 失败姿态由消费侧声明门（IDeclarationGate，G3）裁决，本工具不自持策略。
        return ConsoleCommandHost.ExitOk;
    }) { ErrorLabel = "declared-bundle" });

return host.Run(args, Console.Out, Console.Error);

// 诊断事实可见（AX.NoSilentOutlet）：仅 error、两空格前缀、逐条 stderr（单一实现，替代各分支复制循环）。
static void WriteErrorDiagnostics(System.Collections.Generic.IReadOnlyList<Usharp.Semantics.UsharpDiagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
    {
        if (diagnostic.Severity != Usharp.Semantics.UsharpSeverity.Error) continue;
        Console.Error.WriteLine("  " + diagnostic);
    }
}
