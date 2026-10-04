using System.IO.Pipes;

namespace Common.Diagnostics;

public class NamedPipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly string _pipeName;

    public string? FallbackLogFile { get; private set; }

    public NamedPipeClient(string pipeName)
    {
        _pipeName = PortGuard.Required(pipeName, nameof(pipeName));
    }

    public void Connect(int timeoutMs = 15000)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
        _pipe.Connect(timeoutMs);
        _writer = new StreamWriter(_pipe) { AutoFlush = true };
    }

    public StreamWriter Writer => _writer ?? throw new InvalidOperationException("未连接到管道，请先调用 Connect()");

    private TextWriter? _originalConsoleOut;

    public void RedirectConsoleOut()
    {
        _originalConsoleOut = Console.Out;
        Console.SetOut(Writer);
    }

    public void RestoreConsoleOut()
    {
        if (_originalConsoleOut != null)
        {
            Console.SetOut(_originalConsoleOut);
            _originalConsoleOut = null;
        }
    }

    public void WriteFallback(string message)
    {
        try
        {
            FallbackLogFile ??= Path.Combine(
                Path.GetTempPath(),
                $"FileDriven_PipeFallback_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            File.AppendAllText(FallbackLogFile,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void WriteFallback(string message, Exception ex)
    {
        WriteFallback($"{message}");
        WriteFallback($"  异常类型: {ex.GetType().FullName}");
        WriteFallback($"  异常消息: {ex.Message}");
        if (ex.InnerException != null)
        {
            WriteFallback($"  内部异常: {ex.InnerException.Message}");
        }
        WriteFallback($"  堆栈跟踪: {ex.StackTrace}");
    }

    public void Dispose()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }
    }
}
