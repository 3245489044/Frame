using CallCenter.Core.Router;

namespace CallCenter.Tests;

/// <summary>
/// P4-a 热换重建的唯一构建入口（SingleImplementation）：从 function-libraries.json 清单发现并构建<b>全新</b>
/// ToolRouter（含最新 CSharpTrack）。清单缺失/为空 → 空轨 router（不抛）。验证「改清单 → 重建 → 换引用」的构建侧。
/// </summary>
[TestClass]
public class ToolRouterFactoryTests
{
    [TestMethod]
    public void Build_FromJson_Produces_Router_With_CSharp_Track()
    {
        var typeName = typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary).AssemblyQualifiedName;
        var json = $"{{\"libraries\":[{{\"name\":\"SampleHostLibrary\",\"type\":\"{typeName}\",\"enabled\":true}}]}}";

        var router = ToolRouterFactory.BuildCSharpRouterFromJson(json);

        Assert.IsNotNull(router);
        Assert.IsTrue(router.CanCall("sample_echo"), "发现源注册的 sample_echo 应进入 CSharpTrack。");
        Assert.IsTrue(router.CanCall("sample_add"));
        Assert.IsFalse(router.CanCall("not_there"));
    }

    [TestMethod]
    public void Build_EmptyJson_Returns_Empty_Router_NoThrow()
    {
        var router = ToolRouterFactory.BuildCSharpRouterFromJson("{}");
        Assert.IsNotNull(router);
        Assert.AreEqual(0, router.Tools.Count);
    }

    [TestMethod]
    public void Build_MissingConfigPath_Returns_Empty_Router_NoThrow()
    {
        var router = ToolRouterFactory.BuildCSharpRouter(
            Path.Combine(Path.GetTempPath(), "definitely_missing_" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.IsNotNull(router);
        Assert.AreEqual(0, router.Tools.Count);
    }
}
