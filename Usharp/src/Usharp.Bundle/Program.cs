using System;
using System.IO;
using Usharp.Adapters.Declarations;

// 声明单点编译工具（G1）：<configRoot> <outDir> → manifest / round-spec / reference-schema。
// 唯一编译点 = DeclaredBundleBuilder；本工具只做参数解析与产出上报。
// selfcheck：值映射反例自检（E-4；零测试工程，退出码 0/1 供回归套件断言）。
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

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: Usharp.Bundle <configRoot> <outDir>");
    return 2;
}

var configRoot = args[0];
var outDir = args[1];

try
{
    var artifacts = DeclaredBundleBuilder.Build(configRoot);
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
