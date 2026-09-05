using Python.Runtime;

namespace PythonModel.Bridge;

public static class PythonRuntime
{
    private static readonly object _lock = new();
    private static bool _initialized;
    private static string _pythonHome = string.Empty;
    private static string _pythonModulePath = string.Empty;

    public static bool IsInitialized => _initialized;

    public static string PythonHome => _pythonHome;

    public static string PythonModulePath => _pythonModulePath;

    public static void Initialize(string? pythonHomePath = null, string? pythonModulePath = null)
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            string resolvedHome = PythonEnvironment.ResolveHome(pythonHomePath);
            string resolvedModulePath = PythonEnvironment.ResolveModulePath(pythonModulePath);

            _pythonHome = resolvedHome;
            _pythonModulePath = resolvedModulePath;

            Runtime.PythonDLL = Path.Combine(_pythonHome, PythonEnvironment.FindDllName(_pythonHome));
            PythonEngine.PythonHome = _pythonHome;
            PythonEngine.PythonPath = _pythonModulePath;

            PythonEngine.Initialize();
            _initialized = true;
        }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        lock (_lock)
        {
            if (!_initialized) return;

            try
            {
                PythonEngine.Shutdown();
            }
            catch
            {
            }
            finally
            {
                _initialized = false;
            }
        }
    }

    public static void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "PythonEngine is not initialized. Call PythonRuntime.Initialize() first.");
        }
    }
}
