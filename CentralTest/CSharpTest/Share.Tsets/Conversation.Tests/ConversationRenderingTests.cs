using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.Conversation.Rendering;

namespace Share.Conversation.Tests;

[TestClass]
public class ConversationRenderingTests
{
    [TestMethod]
    public void ToRenderItems_Projects_MessageFields()
    {
        var msg = new ConversationMessage(ConversationRole.Tool, "cline_execute", "cline", "产出", 1, default);
        var items = new[] { msg }.ToRenderItems();

        Assert.AreEqual(1, items.Count);
        var item = items[0];
        Assert.AreEqual(ConversationRole.Tool, item.Role);
        Assert.AreEqual("cline_execute", item.Stage);
        Assert.AreEqual("cline", item.SubAgent);
        Assert.AreEqual("产出", item.Text);
        Assert.AreEqual(1, item.Round);
    }

    [TestMethod]
    public void ToRenderItems_NullInput_ReturnsEmpty()
    {
        var items = ((IEnumerable<ConversationMessage>)null!).ToRenderItems();
        Assert.AreEqual(0, items.Count);

        var fromLog = ((ConversationLog)null!).ToRenderItems();
        Assert.AreEqual(0, fromLog.Count);
    }

    [TestMethod]
    public void ToRenderItems_FromLog_MatchesLogMessages()
    {
        var log = new ConversationLog();
        log.AppendUser("问题");
        log.AppendAssistant("回答");
        log.AppendSubAgent("answer", "answer", "分析");

        var items = log.ToRenderItems();
        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(ConversationRole.User, items[0].Role);
        Assert.AreEqual(ConversationRole.Assistant, items[1].Role);
        Assert.AreEqual("answer", items[2].Stage);
    }

    [TestMethod]
    public void Renderer_InjectedByCaller_IsInvoked()
    {
        // 调用方配置渲染内容：注入假渲染器，验证其收到渲染项并决定输出（IoC 反转契约生效）。
        var captured = new List<ConversationRenderItem>();
        IConversationRenderer renderer = new StubRenderer(captured, "custom-output");
        var items = new[] { new ConversationRenderItem(ConversationRole.User, null, null, "hi", 1) };

        var result = renderer.Render(items);

        Assert.AreEqual("custom-output", result);
        Assert.AreEqual(1, captured.Count);
        Assert.AreEqual("hi", captured[0].Text);
    }

    private sealed class StubRenderer : IConversationRenderer
    {
        private readonly List<ConversationRenderItem> _captured;
        private readonly string _output;

        public StubRenderer(List<ConversationRenderItem> captured, string output)
        {
            _captured = captured;
            _output = output;
        }

        public string Render(IReadOnlyList<ConversationRenderItem> items)
        {
            _captured.AddRange(items);
            return _output;
        }
    }
}
