using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Registry;

namespace WorkFlowMethod.Tests.UsageTests;

[TestClass]
[TestCategory("FunctionLibraryLoader")]
public class ExtensionAssemblyHostTests
{
    [TestMethod]
    public void ResolveType_ForUnknownAssembly_ShouldReturnNull()
    {
        var type = ExtensionAssemblyHost.ResolveType("NoSuch.Namespace.X, NoSuch.Assembly");
        Assert.IsNull(type);
    }

    [TestMethod]
    public void Load_ForUnknownAssembly_ShouldReturnNull()
    {
        var asm = ExtensionAssemblyHost.Load("NoSuch.Assembly");
        Assert.IsNull(asm);
    }

    [TestMethod]
    public void ResolveType_ForExtensionLibrary_ShouldResolveFromDepsJson()
    {
        var deps = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrjBridge.deps.json");
        if (!File.Exists(deps))
        {
            Assert.Inconclusive("运行目录无 PrjBridge.deps.json，跳过扩展库解析断言");
            return;
        }

        var type = ExtensionAssemblyHost.ResolveType("Agent.Launcher.AgentUiLibrary, Agent.Launcher");
        Assert.IsNotNull(type,
            $"扩展库类型 'Agent.Launcher.AgentUiLibrary, Agent.Launcher' 应经 scoped ALC 解析");
    }

    [TestMethod]
    public void ValidateDeployment_ShouldReportNoMissingForKnownLibrary()
    {
        var missing = ExtensionAssemblyHost.ValidateDeployment();
        // 若部署完整，GameEditor.Host.dll 不应缺失（缺失则返回列表中包含它）
        Assert.IsFalse(missing.Contains("lib/net10.0-windows/GameEditor.Host.dll"),
            "GameEditor.Host 运行时资产不应缺失");
    }
}
