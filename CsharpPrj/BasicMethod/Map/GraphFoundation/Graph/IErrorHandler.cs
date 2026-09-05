
#nullable enable

namespace GraphFoundation.Abstractions
{
    public enum GraphErrorType
    {
        NodeAlreadyExists,
        NodeNotFound,
        EdgeAlreadyExists,
        EdgeNotFound,
        TransactionConflict,
        InvalidOperation,
        InternalError
    }

    public class GraphError
    {
        public GraphErrorType Type { get; }
        public string Message { get; }
        public Exception? InnerException { get; }

        public GraphError(GraphErrorType type, string message, Exception? innerException = null)
        {
            Type = type;
            Message = message;
            InnerException = innerException;
        }

        public override string ToString() => $"[{Type}] {Message}";
    }

    public interface IErrorHandler
    {
        bool HandleError(GraphError error);

        GraphError? LastError { get; }
    }

    public class DefaultErrorHandler : IErrorHandler
    {
        private GraphError? _lastError;

        public GraphError? LastError => _lastError;

        public bool HandleError(GraphError error)
        {
            _lastError = error;
            return true;
        }
    }

    public class StrictErrorHandler : IErrorHandler
    {
        private GraphError? _lastError;

        public GraphError? LastError => _lastError;

        public bool HandleError(GraphError error)
        {
            _lastError = error;
            throw new GraphException(error);
        }
    }

    public class GraphException : Exception
    {
        public GraphError GraphError { get; }

        public GraphException(GraphError error) : base(error.Message, error.InnerException)
        {
            GraphError = error;
        }

        public GraphException(GraphErrorType type, string message) : base(message)
        {
            GraphError = new GraphError(type, message);
        }
    }
}
