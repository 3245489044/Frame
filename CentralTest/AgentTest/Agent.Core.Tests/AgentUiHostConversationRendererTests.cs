using Agent.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation.Rendering;
using TConfig;
using UiBackend.Html;

namespace Agent.Core.Tests;

[TestClass]
public class AgentUiHostConversationRendererTests
{
    [TestMethod]
    public void Unattached_IsNull()
    {
        AgentUiHost.DetachConversationRenderer();
        Assert.IsNull(AgentUiHost.ConversationRenderer);
        Assert.IsFalse(AgentUiHost.HasConversationRenderer);
    }

    [TestMethod]
    public void Attach_ReturnsRenderer()
    {
        AgentUiHost.DetachConversationRenderer();
        var r = new HtmlStreamRenderer();
        AgentUiHost.AttachConversationRenderer(r);

        Assert.IsTrue(AgentUiHost.HasConversationRenderer);
        Assert.AreSame(r, AgentUiHost.ConversationRenderer);
    }

    [TestMethod]
    public void Detach_ClearsSlot()
    {
        AgentUiHost.AttachConversationRenderer(new HtmlStreamRenderer());
        AgentUiHost.DetachConversationRenderer();

        Assert.IsNull(AgentUiHost.ConversationRenderer);
        Assert.IsFalse(AgentUiHost.HasConversationRenderer);
    }

    [TestMethod]
    public void ConversationPageDeclaration_AttachDetach()
    {
        AgentUiHost.DetachConversationPageDeclaration();
        Assert.IsNull(AgentUiHost.ConversationPageDeclaration);

        var declaration = new UIConfiguration();
        AgentUiHost.AttachConversationPageDeclaration(declaration);

        Assert.AreSame(declaration, AgentUiHost.ConversationPageDeclaration);

        AgentUiHost.DetachConversationPageDeclaration();
        Assert.IsNull(AgentUiHost.ConversationPageDeclaration);
    }
}
