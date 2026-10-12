using System;
using System.IO;
using Usharp.Adapters.Declarations;

// 声明单点编译工具（G1）：<configRoot> <outDir> → manifest / round-spec / reference-schema。
// 唯一编译点 = DeclaredBundleBuilder；本工具只做参数解析与产出上报。
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

    // 生成 = 机械操作：声明编译 error 不判生成失败（产物流已落盘，事实可读）；
    // 失败姿态由消费侧声明门（IDeclarationGate，G3）裁决，本工具不自持策略。
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"declared-bundle 失败: {ex.Message}");
    return 2;
}
