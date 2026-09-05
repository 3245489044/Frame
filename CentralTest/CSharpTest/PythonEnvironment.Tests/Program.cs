using Python.Runtime;
using PythonModel.Bridge;

Console.WriteLine("=== sys.path 诊断 ===\n");

Runtime.PythonDLL = @"C:\Users\Ljy\AppData\Local\Programs\Python\Python313\python313.dll";
PythonEngine.PythonHome = @"C:\Users\Ljy\AppData\Local\Programs\Python\Python313";
PythonEngine.PythonPath = PythonEnvironment.ResolveModulePath();

Console.WriteLine($"PythonEngine.PythonPath = {PythonEngine.PythonPath}\n");

PythonEngine.Initialize();

using (Py.GIL())
{
    dynamic sys = Py.Import("sys");
    Console.WriteLine("sys.path:");
    foreach (var p in sys.path)
        Console.WriteLine($"  {p}");

    Console.WriteLine("\n--- 尝试各种导入方式 ---");

    try
    {
        dynamic m1 = Py.Import("PythonModel.DeepSeekInvoke");
        Console.WriteLine("[方式1] Py.Import(\"PythonModel.DeepSeekInvoke\")  → 成功 ✅");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[方式1] Py.Import(\"PythonModel.DeepSeekInvoke\")  → 失败 ❌ {ex.Message}");
    }

    try
    {
        dynamic m2 = Py.Import("DeepSeekInvoke");
        Console.WriteLine("[方式2] Py.Import(\"DeepSeekInvoke\")  → 成功 ✅");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[方式2] Py.Import(\"DeepSeekInvoke\")  → 失败 ❌ {ex.Message}");
    }
}

PythonEngine.Shutdown();
