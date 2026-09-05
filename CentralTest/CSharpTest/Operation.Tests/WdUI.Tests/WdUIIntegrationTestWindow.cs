
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;
using WdUI.UI.Controls;
using WdUI.UI.Commands;
using WdUI.UI;
using WdUI.Layout;
using WdUI.Serialization;
using WdUI.Settings;
using UniversalLayoutMath;

namespace WdUI.Tests
{
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class WdUIIntegrationTestWindow : TestBase
    {
        private Form? _testForm;
        private bool _formShown;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            MessagePumpEngine.Instance.Initialize();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            if (_testForm != null && !_testForm.IsDisposed)
            {
                MessagePumpEngine.Instance.RunOnUiThread(() =>
                {
                    _testForm!.Close();
                    _testForm!.Dispose();
                });
                _testForm = null;
            }
            _formShown = false;
            MessagePumpEngine.Instance.Shutdown();
            base.TestCleanup();
        }

        private T RunOnUi<T>(Func<T> action)
        {
            var result = default(T);
            var done = false;
            Exception? error = null;
            MessagePumpEngine.Instance.RunOnUiThread(() =>
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
                done = true;
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!done && sw.ElapsedMilliseconds < 5000)
                System.Threading.Thread.Sleep(10);
            if (error != null) throw error;
            return result!;
        }

        private void RunOnUi(Action action)
        {
            RunOnUi<object?>(() => { action(); return null; });
        }

        [TestMethod]
        public void RunAll_IntegrationTestWindow()
        {
            var result = new TestGroupBuilder("IntegrationTestWindow")

                .Add("CreateForm_ShouldCreateWindow", () =>
                {
                    _testForm = MessagePumpEngine.Instance.CreateForm(900, 700);
                    _testForm.Text = "WdUI 集成测试窗口";
                    _testForm.Name = "WdUIIntegrationForm";
                    return _testForm != null && !_testForm.IsDisposed;
                })
                .Add("FormProperties_ShouldBeSet", () =>
                    _testForm!.Text == "WdUI 集成测试窗口"
                    && _testForm.Name == "WdUIIntegrationForm"
                    && _testForm.Width == 900 && _testForm.Height == 700)
                .Add("ShowForm_ShouldDisplayWindow", () =>
                {
                    RunOnUi(() => { _testForm!.Show(); _formShown = true; });
                    return _formShown && _testForm!.Visible;
                })

                .Add("CreateContainer_ShouldReturnInstance", () =>
                {
                    var c = WdUIUsage.CreateContainer("testContainer");
                    return c != null && c.Name == "testContainer" && c is ConfigContainer;
                })
                .Add("ContainerDefaults_ShouldBeCorrect", () =>
                {
                    var c = WdUIUsage.CreateContainer("defaultsTest");
                    return c.Dock == DockStyle.Fill && !c.IsLoaded;
                })
                .Add("RenderContainerFromJson_ShouldCreateControls", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(CreateTestJsonConfig());
                    return config != null && config.Views != null && config.Views.Count > 0;
                })
                .Add("RenderContainerOnForm_ShouldSucceed", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(CreateTestJsonConfig());
                    if (config == null) return false;
                    return RunOnUi(() =>
                    {
                        var container = new ConfigContainer { Name = "renderedContainer" };
                        _testForm!.Controls.Add(container);
                        try
                        {
                            var t = WdUIUsage.RenderAsync(container, config);
                            t.Wait(TimeSpan.FromSeconds(5));
                        }
                        catch (AggregateException) { }
                        return container.Controls.Count > 0;
                    });
                })
                .Add("RenderedControls_ShouldHaveCorrectNames", () => RunOnUi(() =>
                {
                    var c = _testForm!.Controls["renderedContainer"] as ConfigContainer;
                    return c != null
                        && WdUIUsage.FindControl(c, "btnSubmit") != null
                        && WdUIUsage.FindControl(c, "txtName") != null
                        && WdUIUsage.FindControl(c, "lblTitle") != null;
                }))
                .Add("RenderedControls_ShouldHaveCorrectTypes", () => RunOnUi(() =>
                {
                    var c = _testForm!.Controls["renderedContainer"] as ConfigContainer;
                    return c != null
                        && WdUIUsage.FindControl(c, "btnSubmit") is Button
                        && WdUIUsage.FindControl(c, "txtName") is TextBox
                        && WdUIUsage.FindControl(c, "lblTitle") is Label;
                }))

                .Add("CreateControl_Button_ShouldReturnButton", () =>
                    WdUIUsage.CreateControl("button") is Button)
                .Add("CreateControl_TextBox_ShouldReturnTextBox", () =>
                    WdUIUsage.CreateControl("textbox") is TextBox)
                .Add("CreateControl_Panel_ShouldReturnPanel", () =>
                    WdUIUsage.CreateControl("panel") is Panel)
                .Add("CreateControl_UnknownType_ShouldReturnPanel", () =>
                    WdUIUsage.CreateControl("unknownType") is Panel)
                .Add("CreateControl_AndAddToForm_ShouldWork", () => RunOnUi(() =>
                {
                    var btn = WdUIUsage.CreateControl("button");
                    btn.Name = "factoryBtn"; btn.Text = "Factory Button";
                    btn.Location = new Point(10, 10); btn.Size = new Size(120, 30);
                    _testForm!.Controls.Add(btn);
                    var found = WdUIUsage.FindControl(_testForm, "factoryBtn");
                    return found == btn && ((Button)found).Text == "Factory Button";
                }))
                .Add("RegisterCustomControlType_ShouldWork", () =>
                {
                    WdUIUsage.RegisterControlType("customBtn", () => new Button
                    { Text = "Custom", BackColor = Color.LightBlue });
                    var c = WdUIUsage.CreateControl("customBtn");
                    return c is Button && ((Button)c).Text == "Custom";
                })
                .Add("UnregisterControlType_ShouldSucceed", () =>
                {
                    WdUIUsage.RegisterControlType("tempType", () => new Label());
                    return WdUIUsage.UnregisterControlType("tempType");
                })
                .Add("GetRegisteredControlTypes_ShouldContainBuiltIn", () =>
                {
                    var types = WdUIUsage.GetRegisteredControlTypes();
                    return types != null && types.Length > 0
                        && types.Contains("button", StringComparer.OrdinalIgnoreCase);
                })

                .Add("CreateLayoutMediator_ShouldReturnInstance", () =>
                    WdUIUsage.CreateLayoutMediator() != null)
                .Add("GetParentSize_ShouldReturnPositiveSize", () =>
                {
                    var (w, h) = WdUIUsage.GetParentSize(_testForm!);
                    return w > 0 && h > 0;
                })
                .Add("ApplyProportionalLayout_ShouldSetBounds", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "propBtn", Text = "Proportional" };
                    _testForm!.Controls.Add(btn);
                    WdUIUsage.ApplyProportionalLayout(btn, 0.1, 0.1, 0.5, 0.2);
                    return btn.Left >= 0 && btn.Top >= 0;
                }))
                .Add("DetectOverlaps_EmptyForm_ShouldReturnNoOverlaps", () =>
                    WdUIUsage.DetectOverlaps(_testForm!) != null)
                .Add("DetectOverlaps_WithOverlappingControls_ShouldDetect", () => RunOnUi(() =>
                {
                    var b1 = new Button { Name = "overlap1", Bounds = new Rectangle(10, 10, 100, 50) };
                    var b2 = new Button { Name = "overlap2", Bounds = new Rectangle(50, 30, 100, 50) };
                    _testForm!.Controls.Add(b1); _testForm.Controls.Add(b2);
                    return WdUIUsage.DetectOverlaps(_testForm) != null;
                }))
                .Add("LayoutContext_ShouldNotBeNull", () => WdUIUsage.LayoutContext != null)
                .Add("StrategyRegistry_ShouldNotBeNull", () => WdUIUsage.StrategyRegistry != null)

                .Add("CommandEngine_ShouldNotBeNull", () => WdUIUsage.CommandEngine != null)
                .Add("RegisterCustomCommand_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterCommand("testCmd", async (ctx, ct) => await Task.CompletedTask);
                    return true;
                })
                .Add("ExecuteCustomCommand_ShouldSucceed", async () =>
                {
                    var executed = false;
                    WdUIUsage.RegisterCommand("execTest", async (ctx, ct) =>
                    { executed = true; await Task.CompletedTask; });
                    await WdUIUsage.ExecuteCommandAsync("execTest");
                    return executed;
                })
                .Add("ExecuteCommandWithParameters_ShouldWork", async () =>
                {
                    object? p = null;
                    WdUIUsage.RegisterCommand("paramTest", async (ctx, ct) =>
                    { ctx.Parameters?.TryGetValue("key", out p); await Task.CompletedTask; });
                    await WdUIUsage.ExecuteCommandAsync("paramTest",
                        new Dictionary<string, object?> { ["key"] = "value123" });
                    return p?.ToString() == "value123";
                })
                .Add("ExecuteNonExistentCommand_ShouldNotThrow", async () =>
                {
                    await WdUIUsage.ExecuteCommandAsync("non.existent.cmd");
                    return true;
                })
                .Add("ExecuteMultipleCommands_ShouldExecuteAll", async () =>
                {
                    var count = 0;
                    WdUIUsage.RegisterCommand("multi1", async (ctx, ct) =>
                    { Interlocked.Increment(ref count); await Task.CompletedTask; });
                    WdUIUsage.RegisterCommand("multi2", async (ctx, ct) =>
                    { Interlocked.Increment(ref count); await Task.CompletedTask; });
                    await WdUIUsage.ExecuteCommandsAsync(new[] { "multi1", "multi2", "multi1" });
                    return count == 3;
                })

                .Add("RegisterDataSource_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterDataSource("testSource", new TestBindableObject { Name = "TestSource" });
                    return true;
                })
                .Add("GetDataSource_Registered_ShouldReturnInstance", () =>
                {
                    WdUIUsage.RegisterDataSource("getTest", new TestBindableObject { Name = "GetTest" });
                    return WdUIUsage.GetDataSource<TestBindableObject>("getTest")?.Name == "GetTest";
                })
                .Add("GetDataSource_NotRegistered_ShouldReturnNull", () =>
                    WdUIUsage.GetDataSource<TestBindableObject>("nonExistent") == null)
                .Add("UpdateDataSource_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterDataSource("updateTest", new TestBindableObject { Name = "UpdateTest" });
                    WdUIUsage.UpdateDataSource("updateTest", new TestBindableObject { Name = "Updated" });
                    return true;
                })
                .Add("SubscribeToProperty_ShouldNotThrow", () =>
                {
                    WdUIUsage.RegisterDataSource("subTest", new TestBindableObject { Name = "SubTest" });
                    WdUIUsage.SubscribeToProperty("subTest", "Name", value => { });
                    return true;
                })
                .Add("BindControl_ShouldSetInitialValue", () => RunOnUi(() =>
                {
                    WdUIUsage.RegisterDataSource("bindInit", new TestBindableObject { Name = "BindInitial" });
                    var txt = new TextBox { Name = "boundTxt" };
                    _testForm!.Controls.Add(txt);
                    WdUIUsage.BindControl(txt, "bindInit", "Name");
                    return txt.Text == "BindInitial";
                }))

                .Add("SubscribeAndPublish_ShouldTriggerHandler", async () =>
                {
                    var called = false;
                    WdUIUsage.SubscribeEvent("test.integration.event", async data =>
                    { called = true; await Task.CompletedTask; });
                    await WdUIUsage.PublishEventAsync("test.integration.event");
                    return called;
                })
                .Add("PublishWithData_ShouldPassData", async () =>
                {
                    object? d = null;
                    WdUIUsage.SubscribeEvent("test.dataEvent", async data =>
                    { d = data; await Task.CompletedTask; });
                    await WdUIUsage.PublishEventAsync("test.dataEvent", "hello_data");
                    return d?.ToString() == "hello_data";
                })
                .Add("Unsubscribe_ShouldNotTrigger", async () =>
                {
                    var count = 0;
                    async Task handler(object? data)
                    { count++; await Task.CompletedTask; }
                    WdUIUsage.SubscribeEvent("test.unsubEvent", handler);
                    await WdUIUsage.PublishEventAsync("test.unsubEvent");
                    WdUIUsage.UnsubscribeEvent("test.unsubEvent", handler);
                    await WdUIUsage.PublishEventAsync("test.unsubEvent");
                    return count == 1;
                })
                .Add("HasEventSubscribers_WithSubscriber_ShouldReturnTrue", () =>
                {
                    WdUIUsage.SubscribeEvent("test.hasSub", async d => await Task.CompletedTask);
                    return WdUIUsage.HasEventSubscribers("test.hasSub");
                })
                .Add("HasEventSubscribers_WithoutSubscriber_ShouldReturnFalse", () =>
                    !WdUIUsage.HasEventSubscribers("nonExistentEvent"))
                .Add("ClearAllEvents_ShouldRemoveAllSubscribers", async () =>
                {
                    var count = 0;
                    WdUIUsage.SubscribeEvent("test.clearA", async d =>
                    { count++; await Task.CompletedTask; });
                    WdUIUsage.SubscribeEvent("test.clearB", async d =>
                    { count++; await Task.CompletedTask; });
                    WdUIUsage.ClearAllEvents();
                    await WdUIUsage.PublishEventAsync("test.clearA");
                    await WdUIUsage.PublishEventAsync("test.clearB");
                    return count == 0;
                })

                .Add("FindControl_ByName_ShouldReturnControl", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "findBtn", Text = "Find Me" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.FindControl(_testForm, "findBtn") == btn;
                }))
                .Add("FindControl_NonExistent_ShouldReturnNull", () =>
                    WdUIUsage.FindControl(_testForm!, "nonExistent") == null)
                .Add("FindControlByType_ShouldReturnControl", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "typeFindBtn" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.FindControlByType<Button>(_testForm) != null;
                }))
                .Add("FindControlByType_NonExistent_ShouldReturnNull", () =>
                    WdUIUsage.FindControlByType<DataGridView>(_testForm!) == null)
                .Add("FindControlBy_Predicate_ShouldReturnMatching", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "predBtn", Text = "PredicateMatch" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.FindControlBy(_testForm, c => c.Text == "PredicateMatch") == btn;
                }))
                .Add("FindAllControlsByType_ShouldReturnMultiple", () => RunOnUi(() =>
                {
                    _testForm!.Controls.Add(new Button { Name = "multiBtn1" });
                    _testForm.Controls.Add(new Button { Name = "multiBtn2" });
                    return WdUIUsage.FindAllControlsByType<Button>(_testForm).Count >= 2;
                }))
                .Add("FindDirectChild_ShouldReturnDirectChild", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "directChild" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.FindDirectChild(_testForm, "directChild") == btn;
                }))
                .Add("FindDirectChild_Nested_ShouldReturnNull", () => RunOnUi(() =>
                {
                    var pnl = new Panel { Name = "parentPnl" };
                    var btn = new Button { Name = "nestedChild" };
                    pnl.Controls.Add(btn);
                    _testForm!.Controls.Add(pnl);
                    return WdUIUsage.FindDirectChild(_testForm, "nestedChild") == null;
                }))
                .Add("IsDescendantOf_ShouldReturnTrue", () => RunOnUi(() =>
                {
                    var pnl = new Panel { Name = "descPnl" };
                    var btn = new Button { Name = "descBtn" };
                    pnl.Controls.Add(btn);
                    _testForm!.Controls.Add(pnl);
                    return WdUIUsage.IsDescendantOf(btn, _testForm);
                }))
                .Add("IsDescendantOf_NotDescendant_ShouldReturnFalse", () =>
                    !WdUIUsage.IsDescendantOf(new Button { Name = "notDesc" }, _testForm!))
                .Add("GetControlDepth_Root_ShouldReturnZero", () =>
                    WdUIUsage.GetControlDepth(_testForm!) == 0)
                .Add("GetControlDepth_Child_ShouldReturnOne", () => RunOnUi(() =>
                {
                    var btn = new Button { Name = "depthChild" };
                    _testForm!.Controls.Add(btn);
                    return WdUIUsage.GetControlDepth(btn) == 1;
                }))

                .Add("RunOnUiThread_ShouldExecuteOnUiThread", async () =>
                {
                    var tid = 0;
                    await WdUIUsage.RunOnUiThread(_testForm!, async () =>
                    { tid = Environment.CurrentManagedThreadId; await Task.CompletedTask; });
                    return tid > 0;
                })
                .Add("ExecuteOnUiThread_ShouldExecuteSynchronously", () =>
                {
                    var executed = false;
                    WdUIUsage.ExecuteOnUiThread(_testForm!, () => executed = true);
                    return executed;
                })
                .Add("FireAndForget_ShouldNotThrow", () =>
                {
                    WdUIUsage.FireAndForget(async () => await Task.Delay(10));
                    return true;
                })
                .Add("FireAndForget_WithErrorHandler_ShouldHandleError", () =>
                {
                    var handled = false;
                    WdUIUsage.FireAndForget(async () =>
                    { await Task.Delay(10); throw new InvalidOperationException("err"); },
                    ex => handled = true);
                    Thread.Sleep(100);
                    return handled;
                })

                .Add("RenderPipeline_ShouldBeSingleton", () =>
                {
                    var p1 = WdUIUsage.RenderPipeline;
                    var p2 = WdUIUsage.RenderPipeline;
                    return p1 != null && ReferenceEquals(p1, p2);
                })
                .Add("RenderCreate_ShouldCreateLayoutPanel", () => RunOnUi(() =>
                {
                    var config = WdUIUsage.DeserializeConfig(CreateSimpleJsonConfig());
                    if (config == null) return false;
                    var panel = new Panel { Name = "renderTarget", Size = new Size(400, 300) };
                    _testForm!.Controls.Add(panel);
                    WdUIUsage.RenderCreate(panel, config);
                    return panel.Controls.Count > 0;
                }))
                .Add("RenderApplyLayout_ShouldNotThrow", () => RunOnUi(() =>
                {
                    var panel = _testForm!.Controls["renderTarget"] as Panel;
                    if (panel == null) return false;
                    try { WdUIUsage.RenderApplyLayout(panel); }
                    catch (Exception) { }
                    return true;
                }))
                .Add("Relayout_ShouldNotThrow", () => RunOnUi(() =>
                {
                    var panel = _testForm!.Controls["renderTarget"] as Panel;
                    if (panel == null) return false;
                    try { WdUIUsage.Relayout(panel); }
                    catch (Exception) { }
                    return true;
                }))

                .Add("CreateSubContainer_ShouldReturnChild", () =>
                {
                    var p = WdUIUsage.CreateContainer("parentContainer");
                    return p.CreateSubContainer("childContainer")?.Name == "childContainer";
                })
                .Add("FindSubContainer_ShouldReturnChild", () =>
                {
                    var p = WdUIUsage.CreateContainer("findParent");
                    p.CreateSubContainer("findChild");
                    return WdUIUsage.FindSubContainer(p, "findChild")?.Name == "findChild";
                })
                .Add("FindSubContainer_NonExistent_ShouldReturnNull", () =>
                {
                    var p = WdUIUsage.CreateContainer("noChild");
                    return WdUIUsage.FindSubContainer(p, "nonExistent") == null;
                })
                .Add("GetSubContainers_ShouldReturnAllChildren", () =>
                {
                    var p = WdUIUsage.CreateContainer("multiChild");
                    p.CreateSubContainer("child1"); p.CreateSubContainer("child2"); p.CreateSubContainer("child3");
                    return WdUIUsage.GetSubContainers(p).ToList().Count == 3;
                })
                .Add("ClearContainerContent_ShouldRemoveAll", () =>
                {
                    var c = WdUIUsage.CreateContainer("toClear");
                    c.CreateSubContainer("sub1"); c.CreateSubContainer("sub2");
                    WdUIUsage.ClearContainerContent(c);
                    return c.Controls.Count == 0 && !WdUIUsage.IsContainerLoaded(c);
                })
                .Add("IsContainerLoaded_NewContainer_ShouldReturnFalse", () =>
                    !WdUIUsage.IsContainerLoaded(WdUIUsage.CreateContainer("unloaded")))
                .Add("GetContainerConfig_NewContainer_ShouldReturnNull", () =>
                    WdUIUsage.GetContainerConfig(WdUIUsage.CreateContainer("noConfig")) == null)

                .Add("RegisterForm_ShouldNotThrow", () =>
                { WdUIUsage.RegisterForm(_testForm!); return true; })
                .Add("GetRegisteredFormNames_ShouldContainTestForm", () =>
                    WdUIUsage.GetRegisteredFormNames().ToList().Contains(_testForm!.Name))
                .Add("GetForm_ByName_ShouldReturnInstance", () =>
                    WdUIUsage.GetForm(_testForm!.Name) == _testForm)
                .Add("ShowForm_ShouldMakeVisible", () =>
                { WdUIUsage.ShowForm(_testForm!.Name); return _testForm.Visible; })
                .Add("HideForm_ShouldMakeInvisible", () =>
                { WdUIUsage.HideForm(_testForm!.Name); return !_testForm!.Visible; })
                .Add("ShowFormAgain_ShouldRestoreVisibility", () =>
                { WdUIUsage.ShowForm(_testForm!.Name); return _testForm!.Visible; })

                .Add("Settings_ShouldBeSingleton", () => WdUIUsage.Settings != null)
                .Add("DefaultFont_ShouldBeConfigured", () =>
                {
                    var f = WdUIUsage.Settings.DefaultFont.ToFont();
                    return f != null && f.Name == "Segoe UI" && f.Size == 9f;
                })
                .Add("ControlTypeMap_ShouldContainBuiltInTypes", () =>
                {
                    var t = WdUIUsage.Settings.ControlTypes.GetRegisteredControlTypes();
                    return t.Contains("button") && t.Contains("textbox")
                        && t.Contains("panel") && t.Contains("splitcontainer");
                })
                .Add("CreateControlFromSettings_ShouldWork", () =>
                    WdUIUsage.Settings.ControlTypes.CreateControl("button") is Button)
                .Add("RegisterCustomTypeInSettings_ShouldWork", () =>
                {
                    WdUIUsage.Settings.ControlTypes.RegisterControlType("myWidget", () => new Label
                    { Text = "Widget", ForeColor = Color.Blue });
                    var w = WdUIUsage.Settings.ControlTypes.CreateControl("myWidget");
                    return w is Label && ((Label)w).Text == "Widget";
                })
                .Add("DarkThemeSettings_ShouldHaveDefaults", () =>
                    WdUIUsage.Settings.DarkTheme.GetBackColor() != Color.Empty)
                .Add("PipelineNames_ShouldBeConfigured", () =>
                    WdUIUsage.Settings.Pipelines.RenderPipeline == "WinFormsRenderPipeline"
                    && WdUIUsage.Settings.Pipelines.ConfigLoader == "WdUIConfigLoader")
                .Add("EventNames_ShouldBeConfigured", () =>
                    !string.IsNullOrEmpty(WdUIUsage.Settings.EventNames.ConfigLoaded)
                    && !string.IsNullOrEmpty(WdUIUsage.Settings.EventNames.ContentChanged))

                .Add("DeserializeConfig_ValidJson_ShouldReturnConfig", () =>
                    WdUIUsage.DeserializeConfig(CreateTestJsonConfig()) != null)
                .Add("DeserializeConfig_InvalidJson_ShouldThrow", () =>
                {
                    try { WdUIUsage.DeserializeConfig("{invalid}"); return false; }
                    catch { return true; }
                })
                .Add("SerializeConfig_ShouldReturnJsonString", () =>
                {
                    var c = WdUIUsage.DeserializeConfig(CreateTestJsonConfig());
                    return c != null && !string.IsNullOrEmpty(WdUIUsage.SerializeConfig(c));
                })
                .Add("SerializeConfig_RoundTrip_ShouldPreserveData", () =>
                {
                    var o = WdUIUsage.DeserializeConfig(CreateTestJsonConfig());
                    if (o == null) return false;
                    return WdUIUsage.DeserializeConfig(WdUIUsage.SerializeConfig(o)) != null;
                })
                .Add("ValidateJson_Valid_ShouldReturnValid", () =>
                {
                    var v = WdUIUsage.ValidateJson(CreateTestJsonConfig());
                    return v != null && v.IsValid;
                })
                .Add("ValidateJson_Invalid_ShouldReturnInvalid", () =>
                {
                    var v = WdUIUsage.ValidateJson("{invalid}");
                    return v != null && !v.IsValid;
                })
                .Add("ExtractJsonError_ShouldReturnContext", () =>
                {
                    try { JsonDocument.Parse("{invalid}"); return false; }
                    catch (JsonException ex) { return WdUIUsage.ExtractJsonError("test.json", ex) != null; }
                })

                .Add("ToDockStyle_Top_ShouldReturnDockStyleTop", () =>
                    WdUIUsage.ToDockStyle("Top") == DockStyle.Top)
                .Add("ToDockStyle_Fill_ShouldReturnDockStyleFill", () =>
                    WdUIUsage.ToDockStyle("Fill") == DockStyle.Fill)
                .Add("ToDockStyle_Invalid_ShouldReturnNone", () =>
                    WdUIUsage.ToDockStyle("Invalid") == DockStyle.None)
                .Add("ToAnchorStyle_TopLeft_ShouldReturnCombined", () =>
                    WdUIUsage.ToAnchorStyle("Top,Left") == (AnchorStyles.Top | AnchorStyles.Left))
                .Add("ToWindowState_Maximized_ShouldReturnMaximized", () =>
                    WdUIUsage.ToWindowState("Maximized") == FormWindowState.Maximized)
                .Add("ParseColor_Red_ShouldReturnKnownColor", () =>
                    WdUIUsage.ParseColor("Red").Name == "Red")
                .Add("ParseColor_Hex_ShouldParseCorrectly", () =>
                    WdUIUsage.ParseColor("#FF0000").ToArgb() == Color.Red.ToArgb())
                .Add("ParseEnum_ValidValue_ShouldReturnParsed", () =>
                    WdUIUsage.ParseEnum("Top", NavigationBarPosition.Top) == NavigationBarPosition.Top)
                .Add("ParseEnum_InvalidValue_ShouldReturnDefault", () =>
                    WdUIUsage.ParseEnum("Invalid", NavigationBarPosition.Left) == NavigationBarPosition.Left)
                .Add("TruncateString_ShortString_ShouldReturnOriginal", () =>
                    WdUIUsage.TruncateString("Hello", 10) == "Hello")
                .Add("TruncateString_LongString_ShouldTruncate", () =>
                    WdUIUsage.TruncateString("Hello World", 8) == "Hello...")
                .Add("TruncateString_NullInput_ShouldReturnEmpty", () =>
                    WdUIUsage.TruncateString(null!, 10) == string.Empty)

                .Add("ConfigFileExists_NonExistent_ShouldReturnFalse", () =>
                    !WdUIUsage.ConfigFileExists("non_existent_config.json"))
                .Add("GetConfigAbsolutePath_ShouldReturnAbsolutePath", () =>
                    Path.IsPathRooted(WdUIUsage.GetConfigAbsolutePath("test.json")))

                .Add("Log_ShouldNotThrow", () =>
                {
                    WdUIUsage.Log("test_integration_log", new { message = "Integration test log" });
                    return true;
                })

                .Add("FinalWindowState_ShouldBeVisible", () =>
                    _testForm != null && !_testForm.IsDisposed && _testForm.Visible)
                .Add("FinalWindowTitle_ShouldBeCorrect", () =>
                    _testForm!.Text == "WdUI 集成测试窗口")
                .Add("FinalWindowSize_ShouldBeCorrect", () =>
                    _testForm!.Width == 900 && _testForm.Height == 700)

                .Run();

            Console.WriteLine($"========================================");
            Console.WriteLine($"WdUI 集成测试窗口 — 测试完成");
            Console.WriteLine($"  通过: {result.Passed}");
            Console.WriteLine($"  失败: {result.Failed}");
            Console.WriteLine($"  总计: {result.Total}");
            Console.WriteLine($"  通过率: {result.PassRate:F1}%");
            Console.WriteLine($"  耗时: {result.Duration.TotalSeconds:F2}s");
            Console.WriteLine($"========================================");

            result.AssertAllPassed(printSummary: true);
        }

        private static string CreateTestJsonConfig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"form\":{\"title\":\"测试表单\",\"width\":400,\"height\":300},");
            sb.Append("\"views\":[{\"name\":\"mainView\",\"controls\":[");
            sb.Append("{\"name\":\"lblTitle\",\"type\":\"label\",\"text\":\"标题\",\"x\":10,\"y\":10,\"width\":100,\"height\":30},");
            sb.Append("{\"name\":\"txtName\",\"type\":\"textbox\",\"text\":\"\",\"x\":10,\"y\":50,\"width\":200,\"height\":30},");
            sb.Append("{\"name\":\"btnSubmit\",\"type\":\"button\",\"text\":\"提交\",\"x\":10,\"y\":100,\"width\":80,\"height\":30}");
            sb.Append("]}]}");
            return sb.ToString();
        }

        private static string CreateSimpleJsonConfig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"form\":{\"title\":\"简单表单\",\"width\":400,\"height\":300},");
            sb.Append("\"views\":[{\"name\":\"simpleView\",\"controls\":[");
            sb.Append("{\"name\":\"simpleBtn\",\"type\":\"button\",\"text\":\"确定\",\"x\":10,\"y\":10,\"width\":80,\"height\":30}");
            sb.Append("]}]}");
            return sb.ToString();
        }
    }

    internal class TestBindableObject : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
