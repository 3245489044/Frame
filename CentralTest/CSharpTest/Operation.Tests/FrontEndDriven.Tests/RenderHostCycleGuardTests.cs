using FrameworkAbstractions.ContainerAbstractions;
using FrontEndDriven.Shared;
using FrontEndDriven.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;

namespace FrontEndDriven.Tests;

/// <summary>
/// F1/F2 单测：渲染宿主循环防护 RenderHostCycleGuard + 配置角色 ConfigRoleResolver。
/// 覆盖：命中（Shell 注入自声明作用域）/ 未命中（boot 合法壳 / Content 注入宿主）/ 宿主 name 别名命中。
/// </summary>
[TestClass]
public class RenderHostCycleGuardTests
{
    // 壳配置：声明 Content / Side 两个子作用域宿主（等价 MainLayout 的 contentContainer / sideContainer）
    private static UIConfiguration ShellLayout() => new()
    {
        Views =
        {
            new ViewConfig
            {
                Name = "MainView",
                Controls =
                {
                    new ControlConfig
                    {
                        Type = "container",
                        Name = "contentContainer",
                        Container = new ContainerConfig { SourceType = "state", StateScope = "Content", AutoLoad = false }
                    },
                    new ControlConfig
                    {
                        Type = "container",
                        Name = "sideContainer",
                        Container = new ContainerConfig { SourceType = "state", StateScope = "Side", AutoLoad = false }
                    }
                }
            }
        }
    };

    // 内容配置：无任何宿主节点（等价 ConfigSelector / MathFunctions / WorkflowInputUI）
    private static UIConfiguration ContentConfig() => new()
    {
        Views =
        {
            new ViewConfig
            {
                Name = "ContentView",
                Controls = { new ControlConfig { Type = "panel", Name = "body" } }
            }
        }
    };

    private static ContainerHandle Handle(string scope, string hostKey) => new()
    {
        Id = new ContainerId
        {
            ScopeId = scope,
            HostKey = hostKey,
            EntityKey = $"entity:{scope}",
            ControlName = hostKey
        }
    };

    [TestMethod]
    public void Validate_ShellIntoItsOwnDeclaredScope_Blocked()
    {
        // MainLayout → Content（A={Content,Side}, C={Content,Content,contentContainer} → Content∈A∩C）
        var result = RenderHostCycleGuard.Validate(ShellLayout(), "Content", Handle("Content", "contentContainer"));

        Assert.IsFalse(result.Allowed);
        Assert.IsTrue(result.Hits.Count > 0);
        StringAssert.Contains(result.Error!, "循环宿主注入已拦截");
    }

    [TestMethod]
    public void Validate_ShellIntoItsOwnScope_Passes()
    {
        // boot 合法壳渲染：MainLayout → MainLayout（A={Content,Side}, C={MainLayout,MainLayout,root} → A∩C=∅）
        var result = RenderHostCycleGuard.Validate(ShellLayout(), "MainLayout", Handle("MainLayout", "root"));

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void Validate_HostNameAliasHit_Blocked()
    {
        // 宿主 name 别名命中：B={contentContainer,sideContainer}, C={contentContainer,...} → B∩C≠∅
        var result = RenderHostCycleGuard.Validate(ShellLayout(), "contentContainer", Handle("Content", "contentContainer"));

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public void Validate_ContentIntoHost_Passes()
    {
        // Content 配置（声明空）→ Content 宿主，不拦
        var result = RenderHostCycleGuard.Validate(ContentConfig(), "Content", Handle("Content", "contentContainer"));

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void RoleResolver_Shell_ReturnsShell() => Assert.AreEqual(ConfigRole.Shell, ConfigRoleResolver.GetRole(ShellLayout()));

    [TestMethod]
    public void RoleResolver_Content_ReturnsContent() => Assert.AreEqual(ConfigRole.Content, ConfigRoleResolver.GetRole(ContentConfig()));
}
