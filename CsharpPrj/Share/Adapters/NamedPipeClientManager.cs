
using System;

namespace Common.Diagnostics
{
    public class NamedPipeClientManager : IDisposable
    {
        private NamedPipeClient? _client;
        private bool _disposed;

        public bool IsConnected => _client != null;

        public void Connect(string? pipeName)
        {
            if (string.IsNullOrEmpty(pipeName)) return;

            try
            {
                _client = new NamedPipeClient(pipeName);
                _client.Connect();
                _client.RedirectConsoleOut();
            }
            catch
            {
                _client?.Dispose();
                _client = null;
            }
        }

        public void Disconnect()
        {
            if (_client == null) return;

            try
            {
                _client.RestoreConsoleOut();
                _client.Dispose();
            }
            finally
            {
                _client = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
