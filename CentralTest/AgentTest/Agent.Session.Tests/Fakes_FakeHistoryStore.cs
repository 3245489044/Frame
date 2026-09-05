using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Share.Conversation;

namespace Agent.Session.Tests;

internal sealed class FakeHistoryStore : IHistoryStore
{
    private readonly Dictionary<string, ConversationLog> _logs = new();

    public void Put(string sessionId, ConversationLog log) => _logs[sessionId] = log;

    public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(_logs.TryGetValue(sessionId, out var l) ? l : null);
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default) { _logs[sessionId] = log; return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(_logs.Keys.ToList());
    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(_logs.Remove(sessionId));
}
