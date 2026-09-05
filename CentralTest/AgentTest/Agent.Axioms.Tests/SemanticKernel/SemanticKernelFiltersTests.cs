using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms.SemanticKernel.Filters;
using Agent.State;
using BasicMethod.Functor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests.SemanticKernel
{
    /// <summary>
    /// SK 横切管道冒烟测试：验证 LLM 调用改走 <c>kernel.InvokeAsync</c> 后，
    /// 取消检查 / LLM 调用门两个 Filter 对每次函数调用生效，且内容正常回流。
    /// 覆盖 <see cref="CancellationFilter"/> 与 <see cref="LlmGateFilter"/>。
    /// Phase 1（缺陷 1.1）：取消检查 / LLM 门不再为进程级静态单例，经 <see cref="AgentRunCancellation"/> 注入 run 作用域，Filter 从作用域读取。
    /// </summary>
    [TestClass]
    public sealed class SemanticKernelFiltersTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            // Phase 1：已无进程级静态钩子（缺陷 1.1 修复），无需复位；作用域在 using 内自动清理。
        }

        private static Kernel NewKernel(params IFunctionInvocationFilter[] filters)
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IChatCompletionService>(new FakeChatCompletion());
            foreach (var f in filters)
                builder.Services.AddSingleton(f);
            return builder.Build();
        }

        private static readonly KernelFunction ChatFunction = KernelFunctionFactory.CreateFromMethod(
            async (Kernel kernel, KernelArguments args, CancellationToken ct) =>
                (await kernel.GetRequiredService<IChatCompletionService>().GetChatMessageContentAsync(
                        (ChatHistory)args["chatHistory"]!, cancellationToken: ct).ConfigureAwait(false)).Content,
            functionName: "ChatCompletion");

        [TestMethod]
        public async Task InvokeAsync_WithCancellationFilter_ThrowsOnHostCancel()
        {
            // 在 run 作用域内注入会抛出的取消检查；SK InvokeAsync 的 filter 从作用域读取。
            using (RunContext.Enter(new RunContext(default)))
            {
                RunContext.Current!.CancellationCheck = () => throw new OperationCanceledException("host cancel");
                var kernel = NewKernel(new CancellationFilter());

                var args = new KernelArguments { ["chatHistory"] = new ChatHistory("u") };
                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await kernel.InvokeAsync(ChatFunction, args));
            }
        }

        [TestMethod]
        public async Task InvokeAsync_WithoutCancellation_ReturnsContent()
        {
            var kernel = NewKernel(new CancellationFilter());

            var args = new KernelArguments { ["chatHistory"] = new ChatHistory("u") };
            var result = await kernel.InvokeAsync(ChatFunction, args);

            Assert.AreEqual(FakeChatCompletion.Content, result.GetValue<string>());
        }

        [TestMethod]
        public async Task InvokeAsync_WithLlmGateFilter_InvokesGate()
        {
            var gateCalled = false;
            using (RunContext.Enter(new RunContext(default)))
            {
                RunContext.Current!.LlmGate = _ => { gateCalled = true; return Task.CompletedTask; };
                var kernel = NewKernel(new LlmGateFilter());

                var args = new KernelArguments { ["chatHistory"] = new ChatHistory("u") };
                await kernel.InvokeAsync(ChatFunction, args);
            }

            Assert.IsTrue(gateCalled);
        }

        private sealed class FakeChatCompletion : IChatCompletionService
        {
            public const string Content = "ok";
            public string? ServiceId => "fake";
            public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                    new[] { new ChatMessageContent(AuthorRole.Assistant, Content) });

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentAsync(
                ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                    new[] { new ChatMessageContent(AuthorRole.Assistant, Content) });

            public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
