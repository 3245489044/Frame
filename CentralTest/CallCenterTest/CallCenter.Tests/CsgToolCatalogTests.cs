using CallCenter.Core.DataPlane;
using CallCenter.Core.Tools;
using CSG.Unified;

namespace CallCenter.Tests;

/// <summary>锁定 CsgToolCatalog：注册唯一性/冲突检测 + CanonicalForm 投影 + NotExposedToModel 语义保留。</summary>
[TestClass]
public class CsgToolCatalogTests
{
    private static CsgFacade _csg = new();
    private static CsgToolCatalog NewCatalog() => new(_csg);

    private static ToolDescriptor Tool(
        string id, string name, string backend = ToolBackend.CSharp,
        ToolFlags flags = ToolFlags.Deterministic, string description = "d")
        => new(id, name, backend, 1, description,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            flags);

    [TestMethod]
    public void Register_Distinct_Tools_Then_Canonical_Is_Valid()
    {
        var catalog = NewCatalog();
        catalog.Register(Tool("tool:csharp:a", "a"));
        catalog.Register(Tool("tool:csharp:b", "b", ToolBackend.TypeScript));

        var form = catalog.ToCanonical();
        var vr = catalog.Validate();

        Assert.AreEqual(2, catalog.Tools.Count);
        Assert.AreEqual(2, form.Nodes.Count);
        Assert.IsTrue(vr.IsValid, "节点图应通过 CSG 一致性校验，errors=" + (vr.Errors?.Count ?? 0));
    }

    [TestMethod]
    public void Register_Duplicate_Id_Throws_Conflict()
    {
        var catalog = NewCatalog();
        catalog.Register(Tool("tool:csharp:cc_echo", "cc_echo"));

        var ex = ThrowsAssert.Throws<InvalidOperationException>(() =>
            catalog.Register(Tool("tool:csharp:cc_echo", "cc_echo")));
        StringAssert.Contains(ex.Message, "重复登记");
    }

    [TestMethod]
    public void RegisterRange_With_Internal_Duplicate_Throws()
    {
        var catalog = NewCatalog();
        var dup = Tool("tool:csharp:x", "x");
        ThrowsAssert.Throws<InvalidOperationException>(() =>
            catalog.RegisterRange(new[] { Tool("tool:csharp:a", "a"), dup, dup }));
    }

    [TestMethod]
    public void Register_MultiRail_Same_Name_Is_Allowed()
    {
        // 同名不同 backend/version → 不同 Id → 多轨并存合法（CSG 版本化前提）。
        var catalog = NewCatalog();
        catalog.Register(Tool("tool:csharp:greet", "greet", ToolBackend.CSharp, ToolFlags.Deterministic));
        catalog.Register(Tool("tool:typescript:greet", "greet", ToolBackend.TypeScript, ToolFlags.Externalized));
        Assert.AreEqual(2, catalog.Tools.Count);
    }

    [TestMethod]
    public void NotExposedToModel_Flag_Is_Preserved_In_Canonical_Metadata()
    {
        var catalog = NewCatalog();
        catalog.Register(Tool("tool:csharp:internal_tool", "internal_tool", flags: ToolFlags.Deterministic | ToolFlags.NotExposedToModel));

        var node = catalog.ToCanonical().Nodes.Single(n => n.Id == "tool:csharp:internal_tool");
        var flags = node.Properties["flags"]?.ToString();

        StringAssert.Contains(flags, "NotExposedToModel");
    }
}
