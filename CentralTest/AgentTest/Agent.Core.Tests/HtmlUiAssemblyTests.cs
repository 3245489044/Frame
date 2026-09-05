using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Observe;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Html;

namespace Agent.Core.Tests;

[TestClass]
public class HtmlUiAssemblyTests
{
    private sealed class FakeBridge : IHtmlChannel
    {
        public Task RenderHtmlAsync(string html, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ObserveOutcome("t", "html:document", 1, AckLevel.Converged, TimeSpan.Zero, ChangeResult.Committed));
        public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> LoopbackProbeAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default) => Task.FromResult("OK");
        public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default) => Task.FromResult("OK");
        public Task<string> InvokeEventAsync(string elementName, string eventName, string? payload) => Task.FromResult("OK");
    }

    private sealed class FakeRegistry : ITrConfigRegistry
    {
        private readonly Dictionary<string, ITrConfig> _map = new(StringComparer.OrdinalIgnoreCase);
        public void Register(string name, ITrConfig config) => _map[name] = config;
        public bool TryGetConfig(string name, out ITrConfig config) => _map.TryGetValue(name, out config!);
        public void Register(string name, Delegate function, string? description = null) { }
        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null) { }
        public void Unregister(string name) => _map.Remove(name);
        public bool Contains(string name) => _map.ContainsKey(name);
        public IEnumerable<string> GetRegisteredNames() => _map.Keys;
        public void Clear() => _map.Clear();
        public Delegate? Find(string functionName) => null;
        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor) { descriptor = null; return false; }
        public bool TryGetDelegate(string name, out Delegate? function) { function = null; return false; }
    }

    private static UIConfiguration UiConfig()
        => new()
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "MainView",
                    Controls = new List<ControlConfig>
                    {
                        new()
                        {
                            Type = "htmlviewer", Name = "conversationHistoryViewer",
                            HtmlPage = new HtmlPageDeclaration
                            {
                                Name = "conversationPage",
                                Width = 1280,
                                Height = 940,
                                Controls = new List<ControlConfig>
                                {
                                    new() { Type = "panel", Name = "sidebar",
                                        Controls = new List<ControlConfig> { new() { Type = "label", Name = "sidebarTitle", Text = "会话 / 任务" } } },
                                },
                            },
                        },
                    },
                },
            },
        };

    [TestMethod]
    public void ExtractConversationPageDeclaration_ReturnsInternalPage()
    {
        AgentUiHost.DetachConversationPageDeclaration();
        var page = HtmlUiAssembly.ExtractConversationPageDeclaration(UiConfig());

        Assert.IsNotNull(page);
        Assert.AreEqual("conversationPage", page!.Views[0].Name);
        Assert.AreEqual("sidebar", page.Views[0].Controls[0].Name);
    }

    [TestMethod]
    public void ExtractConversationPageDeclaration_NoHost_ReturnsNull()
    {
        Assert.IsNull(HtmlUiAssembly.ExtractConversationPageDeclaration(new UIConfiguration()));
        Assert.IsNull(HtmlUiAssembly.ExtractConversationPageDeclaration(null!));
    }

    [TestMethod]
    public void Attach_AssemblesRuntime_AndAttachesConversationDeclaration()
    {
        try
        {
            AgentUiHost.DetachHtmlUi();
            AgentUiHost.DetachConversationPageDeclaration();

            var report = HtmlUiAssembly.Attach(new FakeBridge(), UiConfig(), new FakeRegistry());

            Assert.IsNotNull(report);
            var runtime = AgentUiHost.HtmlUi;
            Assert.IsNotNull(runtime);
            Assert.IsTrue(AgentUiHost.HasHtmlUi);
            Assert.AreSame(runtime, AgentUiHost.HtmlUi);
            // P1 就绪报告：UiConfig 无绑定函数/无 output → 静态项应全 OK（message 项在未 AttachHostMessage 时为 false）
            Assert.IsTrue(report.FunctionsInRegistry);
            Assert.IsTrue(report.OutputTargetUnique);
            // 会话页内部声明已提取注入（含 sidebar）
            var page = AgentUiHost.ConversationPageDeclaration;
            Assert.IsNotNull(page);
            Assert.AreEqual("sidebar", page!.Views[0].Controls[0].Name);
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
            AgentUiHost.DetachConversationPageDeclaration();
        }
    }

    [TestMethod]
    public void AttachTemplatesAndTheme_InjectsJsonTheme()
    {
        try
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();

            var templates = new Dictionary<string, string>
            {
                ["theme"] = ":root{--bg:#123456;}",
                ["taskItem"] = "<button>{session}</button>",
            };
            HtmlUiAssembly.AttachTemplatesAndTheme(templates);

            // JSON 主题已注入（不再落回 HtmlDocumentRenderer.DefaultCss 内嵌默认；经 ConfigThemeProvider/ProviderTheme）
            var theme = HtmlTheme.Resolve();
            Assert.IsNotInstanceOfType(theme, typeof(HtmlTheme.DefaultHtmlTheme));
            Assert.IsTrue(theme.Css.Contains("#123456"));
            // 会话模板已注入
            Assert.IsNotNull(AgentUiHost.ConversationTemplates);
            Assert.IsTrue(AgentUiHost.ConversationTemplates!.ContainsKey("taskItem"));
        }
        finally
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();
        }
    }

    [TestMethod]
    public async Task AttachTemplatesAndThemeFromConfigAsync_LoadsAndInjects()
    {
        try
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();

            var tmp = System.IO.Path.GetTempFileName() + ".json";
            await System.IO.File.WriteAllTextAsync(tmp,
                "{\"templates\":{\"theme\":\":root{--bg:#000000;}\",\"message\":\"<p>{text}</p>\"}}");
            var ok = await HtmlUiAssembly.AttachTemplatesAndThemeFromConfigAsync(tmp);
            System.IO.File.Delete(tmp);

            Assert.IsTrue(ok);
            Assert.IsNotInstanceOfType(HtmlTheme.Resolve(), typeof(HtmlTheme.DefaultHtmlTheme));
            Assert.IsTrue(HtmlTheme.Resolve().Css.Contains("#000000"));
            Assert.IsTrue(AgentUiHost.ConversationTemplates!.ContainsKey("message"));
        }
        finally
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();
        }
    }

    [TestMethod]
    public async Task Attach_ProvidesSharedScriptBridge()
    {
        try
        {
            AgentUiHost.DetachHtmlUi();
            var report = HtmlUiAssembly.Attach(new FakeBridge(), UiConfig(), new FakeRegistry());

            Assert.IsNotNull(report);
            var runtime = AgentUiHost.HtmlUi;
            Assert.IsNotNull(runtime);
            Assert.IsNotNull(runtime.ScriptBridge);
            // 单一实例：脚本桥可执行 json-script 命令（get → DOM 读回；FakeBridge 恒返回 "OK"）
            var result = await runtime.ScriptBridge.ExecuteAsync(new[]
            {
                new HtmlScriptCommand { Action = "get", Target = "conversationHistoryViewer" },
            });
            Assert.IsTrue(result.ToText().Contains("[get]"));
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
        }
    }

    [TestMethod]
    public async Task AttachTemplatesAndThemeFromConfigAsync_WithBrokenUiStructure_StillInjectsTemplates()
    {
        // 回归 8.24：模板注入与 UI 结构整体反序列化解耦——整份 UI 结构含类型失配字段时，
        // 模板/主题仍须注入成功（此前一次 Parse 整份 JSON 抛错 → 槽位 null → 主对话框气泡渲染为空）。
        try
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();

            var tmp = System.IO.Path.GetTempFileName() + ".json";
            await System.IO.File.WriteAllTextAsync(tmp,
                "{\"templates\":{\"theme\":\":root{--bg:#abcdef;}\",\"flowShell\":\"<div>{history|raw}</div>\",\"round\":\"<p>{roundTitle}</p>\"}," +
                "\"form\":{\"width\":\"not-an-int\"},\"views\":[{\"controls\":[{\"type\":\"htmlviewer\",\"name\":\"conversationHistoryViewer\"}]}]}");
            try
            {
                var ok = await HtmlUiAssembly.AttachTemplatesAndThemeFromConfigAsync(tmp);
                Assert.IsTrue(ok, "templates 段存在时应注入成功，即使 UI 结构字段类型失配");
                Assert.IsNotInstanceOfType(HtmlTheme.Resolve(), typeof(HtmlTheme.DefaultHtmlTheme));
                Assert.IsTrue(HtmlTheme.Resolve().Css.Contains("#abcdef"));
                Assert.IsNotNull(AgentUiHost.ConversationTemplates);
                Assert.IsTrue(AgentUiHost.ConversationTemplates!.ContainsKey("flowShell"));
            }
            finally
            {
                System.IO.File.Delete(tmp);
            }
        }
        finally
        {
            HtmlTheme.Detach();
            AgentUiHost.DetachConversationTemplates();
        }
    }

    [TestMethod]
    public void Load_ReturnsFullConfiguration()
    {
        var tmp = System.IO.Path.GetTempFileName() + ".json";
        System.IO.File.WriteAllText(tmp,
            "{\"templates\":{\"message\":\"<p>{text}</p>\"},\"form\":{\"width\":1280,\"height\":940}," +
            "\"views\":[{\"name\":\"MainView\",\"controls\":[{\"type\":\"htmlviewer\",\"name\":\"conversationHistoryViewer\"}]}]}");
        try
        {
            var cfg = HtmlUiAssembly.Load(tmp);
            Assert.IsNotNull(cfg);
            Assert.IsNotNull(cfg!.Templates);
            Assert.IsTrue(cfg.Templates!.ContainsKey("message"));
            Assert.IsTrue(cfg.Views[0].Controls[0].Name == "conversationHistoryViewer");
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }

    [TestMethod]
    public void AgentUiHost_HtmlViewerResolverSlot_StoresAndClears()
    {
        AgentUiHost.AttachHtmlViewerResolver(name => null);
        Assert.IsNotNull(AgentUiHost.HtmlViewerResolver);
        AgentUiHost.DetachHtmlViewerResolver();
        Assert.IsNull(AgentUiHost.HtmlViewerResolver);
    }

    [TestMethod]
    public void AgentUiHost_AgentUiConfigPathSlot_StoresAndClears()
    {
        AgentUiHost.AttachAgentUiConfigPath(@"D:\x\AgentRuntimeUI.json");
        Assert.AreEqual(@"D:\x\AgentRuntimeUI.json", AgentUiHost.AgentUiConfigPath);
        AgentUiHost.DetachAgentUiConfigPath();
        Assert.IsNull(AgentUiHost.AgentUiConfigPath);
    }

    [TestMethod]
    public void Attach_ReportsReadiness_MissingFunctionDetected()
    {
        // P1 就绪契约：声明绑定引用了未注册函数 → FunctionsInRegistry=false 且 Problems 有明确错误
        try
        {
            AgentUiHost.DetachHtmlUi();
            AgentUiHost.DetachConversationPageDeclaration();

            var cfg = UiConfig();
            var host = cfg.Views![0].Controls![0];
            host.Events = new EventConfig();
            host.Events.EventsDictionary["message"] = new List<string> { "AgentUi_NoSuchFunction" };
            host.Events.Bindings = new EventBinding { Output = "conversationHistoryViewer" };

            var report = HtmlUiAssembly.Attach(new FakeBridge(), cfg, new FakeRegistry()); // FakeRegistry 为空 → 函数未注册

            Assert.IsFalse(report.FunctionsInRegistry);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("未在注册库") && p.Contains("AgentUi_NoSuchFunction")));
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
            AgentUiHost.DetachConversationPageDeclaration();
        }
    }

    [TestMethod]
    public void AttachHostMessage_MissingMessageEvent_ReturnsDiagnostic_NotSilent()
    {
        // P1 就绪契约：缺 events.message → 守卫不再静默 return，返回明确失败原因并记入就绪报告
        AgentUiHost.DetachHtmlUi();
        var report = HtmlUiAssembly.Attach(new FakeBridge(), UiConfig(), new FakeRegistry()); // UiConfig 的 conversationHistoryViewer 无 events.message
        var runtime = AgentUiHost.HtmlUi!;
        try
        {
            var err = runtime.AttachHostMessage(new FakeBridge(), HtmlUiAssembly.ConversationHostControl, null);

            Assert.IsNotNull(err);
            StringAssert.Contains(err, "未声明 events.message");
            Assert.IsFalse(runtime.Readiness.MessageSubscribed);
            Assert.IsFalse(runtime.Readiness.HostWriterRegistered);
            Assert.IsTrue(runtime.Readiness.Problems.Any(p => p.Contains("未声明 events.message")));
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
        }
    }

    [TestMethod]
    public void AttachHostMessage_WithMessageEvent_SetsReadiness()
    {
        // P1 就绪契约：声明含 events.message → AttachHostMessage 绑定成功，就绪报告 MessageSubscribed/HostWriterRegistered=true 且返回 null
        AgentUiHost.DetachHtmlUi();
        var cfg = UiConfig();
        var host = cfg.Views![0].Controls![0];
        host.Events = new EventConfig();
        host.Events.EventsDictionary["message"] = new List<string> { "AgentUi_ConversationMessageDispatch" };
        host.Events.Bindings = new EventBinding { Output = "conversationHistoryViewer" };

        var report = HtmlUiAssembly.Attach(new FakeBridge(), cfg, new FakeRegistry());
        var runtime = AgentUiHost.HtmlUi!;
        try
        {
            var err = runtime.AttachHostMessage(new FakeBridge(), HtmlUiAssembly.ConversationHostControl, null);

            Assert.IsNull(err);
            Assert.IsTrue(runtime.Readiness.MessageSubscribed);
            Assert.IsTrue(runtime.Readiness.HostWriterRegistered);
            // AttachHostMessage 成功不应产生「未声明 events.message」问题（静态函数未注册问题由 Attach 独立记入，与此断言无关）
            Assert.IsFalse(runtime.Readiness.Problems.Any(p => p.Contains("未声明 events.message")));
            Assert.IsTrue(report.Summary().Contains("message订阅=OK"));
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
        }
    }

    [TestMethod]
    public void GetHtmlAssemblySnapshot_ReflectsSlotStates()
    {
        // P3/P4 收尾：HTML 装配清单快照聚合各静态槽位状态，供宿主断言装配齐全（消除能力孤岛）
        AgentUiHost.DetachHtmlUi();
        AgentUiHost.DetachConversationTemplates();
        AgentUiHost.DetachConversationPageDeclaration();
        AgentUiHost.DetachHtmlViewerResolver();
        AgentUiHost.DetachConversationHtmlSink();
        AgentUiHost.DetachHtmlDiagnosticsSink();
        AgentUiHost.DetachAgentUiConfigPath();

        try
        {
            var empty = AgentUiHost.GetHtmlAssemblySnapshot();
            Assert.IsFalse(empty.HtmlUiAttached);
            Assert.IsFalse(empty.AllRequiredAttached);

            AgentUiHost.AttachHtmlViewerResolver(_ => null);
            AgentUiHost.AttachConversationHtmlSink(_ => { });
            AgentUiHost.AttachAgentUiConfigPath(@"D:\x.json");
            AgentUiHost.AttachConversationTemplates(new Dictionary<string, string>());
            AgentUiHost.AttachConversationPageDeclaration(new UIConfiguration());
            // HtmlUi 槽位经 Attach 装配
            HtmlUiAssembly.Attach(new FakeBridge(), UiConfig(), new FakeRegistry());

            var snap = AgentUiHost.GetHtmlAssemblySnapshot();
            Assert.IsTrue(snap.HtmlUiAttached);
            Assert.IsTrue(snap.ConversationTemplatesAttached);
            Assert.IsTrue(snap.ConversationPageDeclarationAttached);
            Assert.IsTrue(snap.HtmlViewerResolverAttached);
            Assert.IsTrue(snap.ConversationHtmlSinkAttached);
            Assert.IsTrue(snap.AgentUiConfigPathSet);
            Assert.IsTrue(snap.AllRequiredAttached);
        }
        finally
        {
            AgentUiHost.DetachHtmlUi();
            AgentUiHost.DetachConversationTemplates();
            AgentUiHost.DetachConversationPageDeclaration();
            AgentUiHost.DetachHtmlViewerResolver();
            AgentUiHost.DetachConversationHtmlSink();
            AgentUiHost.DetachHtmlDiagnosticsSink();
            AgentUiHost.DetachAgentUiConfigPath();
        }
    }
}
