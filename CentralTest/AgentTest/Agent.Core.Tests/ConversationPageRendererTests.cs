using System.Collections.Generic;
using Agent.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.Conversation.Rendering;

namespace Agent.Core.Tests;

[TestClass]
public class ConversationPageRendererTests
{
    private static ConversationPageRenderer Renderer() => new();

    [TestInitialize]
    public void Init() => ConversationTemplateTestSupport.AttachFromConfig();

    [TestCleanup]
    public void Cleanup() => ConversationTemplateTestSupport.Detach();

    [TestMethod]
    public void RenderFlow_ProducesFluidConversationPage_WithHistoryTreeAndTaskHistory()
    {
        var histories = new List<HistoryNode>
        {
            new("history-a", new[] { "alpha", "beta" }),
            new("history-b", new[] { "gamma" }),
        };
        var html = Renderer().Render(MainDialogViewType.History, histories, "history-a", "beta",
            new List<ConversationRenderItem> { new(ConversationRole.User, null, null, "你好", 1) });

        // 流式会话页：conv-layout 布局壳（非绝对投影，无固定画布溢出）
        Assert.IsTrue(html.Contains("conv-layout"));
        Assert.IsTrue(html.Contains("session-bar"));
        Assert.IsTrue(html.Contains("conv-main"));
        Assert.IsTrue(html.Contains("bar-title"));
        // 「历史→任务」两级树：history 容器（history-a open）+ task 项（active 高亮，携带 data-history + data-session=taskKey）
        Assert.IsTrue(html.Contains("history-item"));
        Assert.IsTrue(html.Contains("history-header"));
        Assert.IsTrue(html.Contains("history-a"));
        Assert.IsTrue(html.Contains("data-cmd=\"selectTask\""));
        Assert.IsTrue(html.Contains("data-history=\"history-a\""));
        Assert.IsTrue(html.Contains("data-session=\"alpha\""));
        Assert.IsTrue(html.Contains("class=\"task-item active\""));
        // 右侧历史气泡注入
        Assert.IsTrue(html.Contains("msg user"));
    }

    [TestMethod]
    public void RenderHistoryTree_GroupsTasksUnderHistory_WithActive()
    {
        var histories = new List<HistoryNode>
        {
            new("h1", new[] { "t1", "t2" }),
            new("h2", new[] { "t3" }),
        };
        var html = Renderer().RenderHistoryTree(histories, "h1", "t2");

        // 当前历史容器展开，其余折叠（历史头 id 由 .hname span 承载；模板已去除 emoji 图标，见 AgentRuntimeUI.json historyItem）
        Assert.IsTrue(html.Contains("class=\"history-item\" open"));
        Assert.IsTrue(html.Contains("<span class=\"hname\">h1</span>"));
        Assert.IsTrue(html.Contains("data-history=\"h1\""));
        Assert.IsTrue(html.Contains("data-session=\"t1\""));
        Assert.IsTrue(html.Contains("class=\"task-item active\""));
        Assert.IsTrue(html.Contains("data-session=\"t3\""));
    }

    [TestMethod]
    public void RenderHistoryTree_EmptyHistories_ShowsEmptyState()
    {
        var html = Renderer().RenderHistoryTree(new List<HistoryNode>(), "h", "t");
        Assert.IsTrue(html.Contains("（无历史"));
    }

    [TestMethod]
    public void RenderTaskList_DataProvider_OutputsTaskItemsOnly()
    {
        var html = Renderer().RenderTaskList(new[] { "alpha", "beta" }, "beta");

        // 数据提供者：不输出布局壳与标题（标题由声明 sidebarTitle 承担）
        Assert.IsFalse(html.Contains("ui-root"));
        Assert.IsFalse(html.Contains("bar-title"));
        Assert.IsTrue(html.Contains("data-cmd=\"selectTask\""));
        Assert.IsTrue(html.Contains("data-session=\"alpha\""));
        Assert.IsTrue(html.Contains("class=\"task-item active\""));
    }

    [TestMethod]
    public void RenderTaskList_EncodesSessionNames()
    {
        var html = Renderer().RenderTaskList(new[] { "<script>alert(1)</script>" }, "default");

        Assert.IsFalse(html.Contains("<script>alert(1)</script>"));
        Assert.IsTrue(html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"));
    }

    [TestMethod]
    public void RenderFlow_HistoryView_IncludesThreeViewSelector()
    {
        var histories = new List<HistoryNode> { new("h", new[] { "t1" }) };
        var html = Renderer().Render(MainDialogViewType.History, histories, "h", "t1",
            new List<ConversationRenderItem>());

        // 三视图切换器：节点 / 任务 / 历史 三个分段按钮（data-cmd=selectType + data-type），当前 history 项 .active
        Assert.IsTrue(html.Contains("view-switch"));
        Assert.IsTrue(html.Contains("data-cmd=\"selectType\""));
        Assert.IsTrue(html.Contains("data-type=\"node\""));
        Assert.IsTrue(html.Contains("data-type=\"task\""));
        Assert.IsTrue(html.Contains("data-type=\"history\""));
        Assert.IsTrue(html.Contains("view-switch-item active"));
    }

    [TestMethod]
    public void RenderFlow_TaskView_FlattensTaskListFromHistories()
    {
        var histories = new List<HistoryNode>
        {
            new("h1", new[] { "alpha", "beta" }),
            new("h2", new[] { "beta", "gamma" }),
        };
        var html = Renderer().Render(MainDialogViewType.Task, histories, "h1", "beta",
            new List<ConversationRenderItem>());

        // task 视图：左列平铺任务列表（跨历史去重），无「历史→任务」两级树
        Assert.IsTrue(html.Contains("任务（子 agent）"));
        Assert.IsTrue(html.Contains("data-cmd=\"selectTask\""));
        Assert.IsTrue(html.Contains("data-session=\"alpha\""));
        Assert.IsTrue(html.Contains("data-session=\"beta\""));
        Assert.IsTrue(html.Contains("data-session=\"gamma\""));
        Assert.IsFalse(html.Contains("history-item"));
    }

    [TestMethod]
    public void RenderFlow_NodesView_RendersReadonly_NoTaskSelection()
    {
        var html = Renderer().Render(MainDialogViewType.Nodes, new List<HistoryNode>(), "h", "t",
            new List<ConversationRenderItem>(), nodesHtml: "<pre class=\"mono\">node-table</pre>");

        // nodes 视图：只读（无 data-cmd=selectTask 可点选任务），标题标注只读，右区渲染节点数据
        Assert.IsTrue(html.Contains("节点视图（只读）"));
        Assert.IsTrue(html.Contains("<pre class=\"mono\">node-table</pre>"));
        Assert.IsFalse(html.Contains("data-cmd=\"selectTask\""));
    }
}
