
using System;
using System.Collections.Generic;
using Modules.Parsing.Abstractions;
using MetaCommand;

namespace Common.PlaceholderFramework.Handlers
{
    public class HandlerCommandRouter
    {
        private readonly ICommandRouter _router;
        private readonly List<string> _registeredExecutorIds = new();

        public HandlerCommandRouter(ICommandRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public string RegisterHandler(Core.IStructureHandler handler, string? executorId = null)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var executor = new StructureHandlerExecutor(handler, executorId);
            var id = executor.ExecutorId;

            _router.RegisterExecutor(id, executor);
            _registeredExecutorIds.Add(id);

            return id;
        }

        public IReadOnlyList<string> RegisterHandlers(IEnumerable<Core.IStructureHandler> handlers)
        {
            if (handlers == null)
                throw new ArgumentNullException(nameof(handlers));

            var ids = new List<string>();
            foreach (var handler in handlers)
            {
                var id = RegisterHandler(handler);
                ids.Add(id);
            }
            return ids.AsReadOnly();
        }

        public bool UnregisterHandler(string executorId)
        {
            if (string.IsNullOrEmpty(executorId))
                return false;

            _router.UnregisterExecutor(executorId);
            return _registeredExecutorIds.Remove(executorId);
        }

        public void UnregisterAll()
        {
            foreach (var id in _registeredExecutorIds)
            {
                _router.UnregisterExecutor(id);
            }
            _registeredExecutorIds.Clear();
        }

        public IReadOnlyList<string> RegisteredExecutorIds =>
            _registeredExecutorIds.AsReadOnly();

        public int Count => _registeredExecutorIds.Count;
    }
}
