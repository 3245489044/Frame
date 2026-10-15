using System;

namespace Strategy.Refresh.Diagnostics
{
    /// <summary>刷新框架异常基类（诊断 / 调用边界违规）。</summary>
    public class RefreshException : Exception
    {
        /// <inheritdoc />
        public RefreshException(string message) : base(message) { }

        /// <inheritdoc />
        public RefreshException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>登记违规：重复登记（同键异实例）或描述符与源不一致。</summary>
    public sealed class RefreshRegistrationException : RefreshException
    {
        /// <inheritdoc />
        public RefreshRegistrationException(string message) : base(message) { }

        /// <inheritdoc />
        public RefreshRegistrationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>未登记查询：按槽键 / 源标识取用时不存在（调用边界抛，不静默回落）。</summary>
    public sealed class RefreshNotRegisteredException : RefreshException
    {
        /// <inheritdoc />
        public RefreshNotRegisteredException(string message) : base(message) { }

        /// <inheritdoc />
        public RefreshNotRegisteredException(string message, Exception inner) : base(message, inner) { }
    }
}
