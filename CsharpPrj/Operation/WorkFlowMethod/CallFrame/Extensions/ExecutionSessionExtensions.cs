
using WorkFlowMethod.CallFrame.Abstractions;
using WorkFlowMethod.CallFrame.Core;
using WorkFlowMethod.Execution.Session;

namespace WorkFlowMethod.CallFrame.Extensions
{
    public static class ExecutionSessionExtensions
    {
        private const string CallFrameKey = "__CallFrame__";
        private const string CallFrameServiceKey = "__CallFrameService__";

        public static ICallFrame GetOrCreateCallFrame(this ExecutionSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Properties.TryGetValue(CallFrameKey, out var existing) && existing is ICallFrame cf)
                return cf;

            var newCf = new Core.CallFrame(session.SessionId);
            session.Properties[CallFrameKey] = newCf;
            return newCf;
        }

        public static ICallFrame? GetCallFrame(this ExecutionSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Properties.TryGetValue(CallFrameKey, out var existing) && existing is ICallFrame cf)
                return cf;

            return null;
        }

        public static bool RemoveCallFrame(this ExecutionSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Properties.TryGetValue(CallFrameKey, out var existing) && existing is ICallFrame cf)
            {
                cf.Dispose();
                return session.Properties.TryRemove(CallFrameKey, out _);
            }

            return false;
        }

        public static ICallFrameNode? RecordNodeCall(
            this ExecutionSession session,
            string symbol,
            object? input = null,
            object? output = null,
            IEnumerable<string>? labels = null)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var callFrame = session.GetOrCreateCallFrame();

            if (callFrame.Root == null)
                return callFrame.CreateRoot(symbol, input, labels);

            var lastNode = callFrame.GetAllNodes()
                .LastOrDefault(n => n.Outputs.Any());

            if (lastNode != null)
            {
                var child = lastNode.AddChild(symbol, input, output, labels);
                return child;
            }

            return callFrame.Root.AddChild(symbol, input, output, labels);
        }

        public static ICallFrameService GetOrCreateCallFrameService(this ExecutionSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Properties.TryGetValue(CallFrameServiceKey, out var existing) && existing is ICallFrameService service)
                return service;

            var newService = new CallFrameService();
            session.Properties[CallFrameServiceKey] = newService;
            return newService;
        }

        public static IEnumerable<ICallFrameNode> QueryCallFrame(
            this ExecutionSession session,
            Models.CallFrameQuery query)
        {
            var callFrame = session.GetCallFrame();
            if (callFrame == null)
                return Enumerable.Empty<ICallFrameNode>();

            return callFrame.Query(query);
        }
    }
}
