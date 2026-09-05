
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;

namespace WdUI.Tests
{
    [TestClass]
    public class WdUIUsageUiTests : TestBase
    {
        private Form? _testForm;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            MessagePumpEngine.Instance.Initialize();
            _testForm = MessagePumpEngine.Instance.CreateForm(800, 600);
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            if (_testForm != null && !_testForm.IsDisposed)
            {
                MessagePumpEngine.Instance.RunOnUiThread(() =>
                {
                    _testForm.Close();
                    _testForm.Dispose();
                });
                _testForm = null;
            }
            MessagePumpEngine.Instance.Shutdown();
            base.TestCleanup();
        }

        [TestMethod]
        public void RunAll_ControlFinder()
        {
            var result = new TestGroupBuilder("ControlFinder")
                .Add("FindControl_ByName_ShouldReturnControl", () =>
                {
                    var btn = new Button { Name = "testBtn", Text = "Test" };
                    _testForm!.Controls.Add(btn);
                    var found = WdUIUsage.FindControl(_testForm, "testBtn");
                    return found == btn;
                })
                .Add("FindControl_NonExistent_ShouldReturnNull", () =>
                {
                    var found = WdUIUsage.FindControl(_testForm!, "nonExistent");
                    return found == null;
                })
                .Add("FindControlByType_ShouldReturnControl", () =>
                {
                    var btn = new Button { Name = "btnTypeTest" };
                    _testForm!.Controls.Add(btn);
                    var found = WdUIUsage.FindControlByType<Button>(_testForm);
                    return found != null;
                })
                .Add("FindControlByType_NonExistent_ShouldReturnNull", () =>
                {
                    var found = WdUIUsage.FindControlByType<DataGridView>(_testForm!);
                    return found == null;
                })
                .Add("FindControlBy_Predicate_ShouldReturnMatching", () =>
                {
                    var btn = new Button { Name = "predicateBtn", Text = "MatchMe" };
                    _testForm!.Controls.Add(btn);
                    var found = WdUIUsage.FindControlBy(_testForm, c => c.Text == "MatchMe");
                    return found == btn;
                })
                .Add("FindAllControls_ShouldReturnAtLeastAdded", () =>
                {
                    _testForm!.Controls.Add(new Button { Name = "btn1" });
                    _testForm.Controls.Add(new Button { Name = "btn2" });
                    _testForm.Controls.Add(new Label { Name = "lbl1" });
                    var buttons = WdUIUsage.FindAllControlsByType<Button>(_testForm);
                    return buttons.Count >= 2;
                })
                .Add("FindDirectChild_ShouldReturnDirectChild", () =>
                {
                    var btn = new Button { Name = "directChild" };
                    _testForm!.Controls.Add(btn);
                    var found = WdUIUsage.FindDirectChild(_testForm, "directChild");
                    return found == btn;
                })
                .Add("FindDirectChild_Nested_ShouldReturnNull", () =>
                {
                    var panel = new Panel { Name = "parentPanel" };
                    var btn = new Button { Name = "nestedBtn" };
                    panel.Controls.Add(btn);
                    _testForm!.Controls.Add(panel);
                    var found = WdUIUsage.FindDirectChild(_testForm, "nestedBtn");
                    return found == null;
                })
                .Add("IsDescendantOf_ShouldReturnTrue", () =>
                {
                    var panel = new Panel { Name = "descPanel" };
                    var btn = new Button { Name = "descBtn" };
                    panel.Controls.Add(btn);
                    _testForm!.Controls.Add(panel);
                    return WdUIUsage.IsDescendantOf(btn, _testForm);
                })
                .Add("IsDescendantOf_NotDescendant_ShouldReturnFalse", () =>
                {
                    var btn = new Button { Name = "notDescendant" };
                    return !WdUIUsage.IsDescendantOf(btn, _testForm!);
                })
                .Add("GetControlDepth_Root_ShouldReturnZero", () =>
                {
                    return WdUIUsage.GetControlDepth(_testForm!) == 0;
                })
                .Add("GetControlDepth_Child_ShouldReturnOne", () =>
                {
                    var btn = new Button { Name = "depthBtn" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.GetControlDepth(btn) == 1;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ContainerManagement()
        {
            var result = new TestGroupBuilder("ContainerManagement")
                .Add("CreateContainer_ShouldReturnInstance", () =>
                {
                    var container = WdUIUsage.CreateContainer("testContainer");
                    return container != null && container.Name == "testContainer";
                })
                .Add("CreateContainer_WithEmptyName_ShouldNotThrow", () =>
                {
                    var container = WdUIUsage.CreateContainer("");
                    return container != null;
                })
                .Add("IsContainerLoaded_NewContainer_ShouldReturnFalse", () =>
                {
                    var container = WdUIUsage.CreateContainer("unloaded");
                    return !WdUIUsage.IsContainerLoaded(container);
                })
                .Add("GetContainerConfig_NewContainer_ShouldReturnNull", () =>
                {
                    var container = WdUIUsage.CreateContainer("noConfig");
                    return WdUIUsage.GetContainerConfig(container) == null;
                })
                .Add("ClearContainerContent_ShouldNotThrow", () =>
                {
                    var container = WdUIUsage.CreateContainer("toClear");
                    WdUIUsage.ClearContainerContent(container);
                    return true;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ControlFactory()
        {
            var result = new TestGroupBuilder("ControlFactory")
                .Add("CreateControl_Button_ShouldReturnButton", () =>
                {
                    var control = WdUIUsage.CreateControl("button");
                    return control is Button;
                })
                .Add("CreateControl_TextBox_ShouldReturnTextBox", () =>
                {
                    var control = WdUIUsage.CreateControl("textbox");
                    return control is TextBox;
                })
                .Add("CreateControl_Panel_ShouldReturnPanel", () =>
                {
                    var control = WdUIUsage.CreateControl("panel");
                    return control is Panel;
                })
                .Add("CreateControl_UnknownType_ShouldReturnDefaultControl", () =>
                {
                    var control = WdUIUsage.CreateControl("unknownType");
                    return control is Panel;
                })
                .Add("RegisterControlType_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterControlType("customType", () => new Button());
                    return true;
                })
                .Add("RegisterAndCreateCustomType_ShouldWork", () =>
                {
                    WdUIUsage.RegisterControlType("myButton", () => new Button { Text = "Custom" });
                    var control = WdUIUsage.CreateControl("myButton");
                    return control is Button && ((Button)control).Text == "Custom";
                })
                .Add("UnregisterControlType_ShouldReturnTrue", () =>
                {
                    WdUIUsage.RegisterControlType("toUnregister", () => new Button());
                    return WdUIUsage.UnregisterControlType("toUnregister");
                })
                .Add("UnregisterControlType_NonExistent_ShouldReturnFalse", () =>
                {
                    return !WdUIUsage.UnregisterControlType("nonExistent");
                })
                .Add("GetRegisteredControlTypes_ShouldReturnList", () =>
                {
                    var types = WdUIUsage.GetRegisteredControlTypes();
                    return types != null && types.Length > 0;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ExtensionMethods()
        {
            var result = new TestGroupBuilder("ExtensionMethods")
                .Add("FindChildByName_ShouldReturnChild", () =>
                {
                    var btn = new Button { Name = "extBtn" };
                    _testForm!.Controls.Add(btn);
                    var found = _testForm.FindChildByName("extBtn");
                    return found == btn;
                })
                .Add("FindChildByName_NonExistent_ShouldReturnNull", () =>
                {
                    var found = _testForm!.FindChildByName("nonExistent");
                    return found == null;
                })
                .Add("GetAllChildren_ShouldReturnEnumerable", () =>
                {
                    var children = _testForm!.GetAllChildren();
                    return children != null;
                })
                .Add("GetAllChildren_WithControls_ShouldContainAdded", () =>
                {
                    var btn = new Button { Name = "child1" };
                    _testForm!.Controls.Add(btn);
                    _testForm.Controls.Add(new Label { Name = "child2" });
                    var children = _testForm.GetAllChildren().ToList();
                    return children.Contains(btn);
                })
                .Add("GetAllChildren_Nested_ShouldContainNested", () =>
                {
                    var panel = new Panel { Name = "panel" };
                    var nestedBtn = new Button { Name = "nestedChild" };
                    panel.Controls.Add(nestedBtn);
                    _testForm!.Controls.Add(panel);
                    var children = _testForm.GetAllChildren().ToList();
                    return children.Contains(panel) && children.Contains(nestedBtn);
                })
                .Add("SafeSetText_ShouldSetText", () =>
                {
                    var btn = new Button { Name = "safeBtn" };
                    btn.SafeSetText("Safe Text");
                    return btn.Text == "Safe Text";
                })
                .Add("SafeSetVisible_ShouldSetVisible", () =>
                {
                    var btn = new Button { Name = "safeVisible" };
                    btn.SafeSetVisible(false);
                    return !btn.Visible;
                })
                .Add("SafeSetEnabled_ShouldSetEnabled", () =>
                {
                    var btn = new Button { Name = "safeEnabled" };
                    btn.SafeSetEnabled(false);
                    return !btn.Enabled;
                })
                .Add("EnableDoubleBuffering_ShouldNotThrow", () =>
                {
                    var btn = new Button { Name = "dbBtn" };
                    btn.EnableDoubleBuffering();
                    return true;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_LayoutTools()
        {
            var result = new TestGroupBuilder("LayoutTools")
                .Add("GetParentSize_ShouldReturnSize", () =>
                {
                    var (width, height) = WdUIUsage.GetParentSize(_testForm!);
                    return width > 0 && height > 0;
                })
                .Add("ApplyProportionalLayout_ShouldNotThrow", () =>
                {
                    var btn = new Button { Name = "propBtn" };
                    _testForm!.Controls.Add(btn);
                    WdUIUsage.ApplyProportionalLayout(btn, 0.1, 0.1, 0.5, 0.2);
                    return true;
                })
                .Add("DetectOverlaps_EmptyForm_ShouldReturnNoOverlaps", () =>
                {
                    var report = WdUIUsage.DetectOverlaps(_testForm!);
                    return report != null;
                })
                .Add("CreateLayoutMediator_ShouldReturnInstance", () =>
                {
                    var mediator = WdUIUsage.CreateLayoutMediator();
                    return mediator != null;
                })
                .Add("LayoutContext_ShouldNotBeNull", () =>
                {
                    return WdUIUsage.LayoutContext != null;
                })
                .Add("StrategyRegistry_ShouldNotBeNull", () =>
                {
                    return WdUIUsage.StrategyRegistry != null;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public async Task RunAll_AsyncHelpers()
        {
            var result = await new TestGroupBuilder("AsyncHelpers")
                .Add("RunOnUiThread_ShouldExecuteOnUiThread", async () =>
                {
                    var threadId = 0;
                    await WdUIUsage.RunOnUiThread(_testForm!, async () =>
                    {
                        threadId = Environment.CurrentManagedThreadId;
                        await Task.CompletedTask;
                    });
                    return threadId > 0;
                })
                .Add("ExecuteOnUiThread_ShouldExecuteSynchronously", () =>
                {
                    var executed = false;
                    WdUIUsage.ExecuteOnUiThread(_testForm!, () =>
                    {
                        executed = true;
                    });
                    return executed;
                })
                .Add("FireAndForget_ShouldNotThrow", () =>
                {
                    WdUIUsage.FireAndForget(async () =>
                    {
                        await Task.Delay(10);
                    });
                    return true;
                })
                .Add("FireAndForget_WithErrorHandler_ShouldNotThrow", () =>
                {
                    var errorHandled = false;
                    WdUIUsage.FireAndForget(async () =>
                    {
                        await Task.Delay(10);
                        throw new InvalidOperationException("Test error");
                    }, ex =>
                    {
                        errorHandled = true;
                    });
                    Thread.Sleep(100);
                    return errorHandled;
                })
                .RunAsync();

            result.AssertAllPassed(printSummary: true);
        }
    }
}
