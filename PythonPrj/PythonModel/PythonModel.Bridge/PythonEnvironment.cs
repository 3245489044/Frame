using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PythonModel.Bridge;

public static class PythonEnvironment
{
    public static string ResolveHome(string? hint = null)
    {
        if (!string.IsNullOrEmpty(hint) && Directory.Exists(hint))
            return hint;

        string? envHome = Environment.GetEnvironmentVariable("PYTHONHOME");
        if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
            return envHome;

        string? fromPyLauncher = DiscoverViaPyLauncher();
        if (fromPyLauncher != null)
            return fromPyLauncher;

        string? fromRegistry = DiscoverViaRegistry();
        if (fromRegistry != null)
            return fromRegistry;

        string? fromCommonPaths = DiscoverViaCommonPaths();
        if (fromCommonPaths != null)
            return fromCommonPaths;

        string? fromPath = DiscoverViaPath();
        if (fromPath != null)
            return fromPath;

        string embeddedHome = GetEmbeddedHome();
        if (Directory.Exists(embeddedHome))
            return embeddedHome;

        throw new InvalidOperationException(
            $"Python runtime not found. Tried: PYTHONHOME env var, py launcher, " +
            "Windows registry, common install paths, PATH, " +
            $"and embedded path: {embeddedHome}. " +
            "Run setup_python.ps1 to configure the environment.");
    }

    public static string FindDllName(string homePath)
    {
        if (!Directory.Exists(homePath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "python3.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libpython3.so";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libpython3.dylib";

            throw new PlatformNotSupportedException("Unsupported OS platform");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var versionDlls = Directory.GetFiles(homePath, "python*.dll")
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return name != "python3.dll" && name.StartsWith("python", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(f => Path.GetFileName(f))
                .ToArray();

            if (versionDlls.Length > 0)
                return Path.GetFileName(versionDlls[0]);

            if (File.Exists(Path.Combine(homePath, "python3.dll")))
                return "python3.dll";

            return "python3.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return File.Exists(Path.Combine(homePath, "libpython3.so")) ? "libpython3.so" : "libpython3.so";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return File.Exists(Path.Combine(homePath, "libpython3.dylib")) ? "libpython3.dylib" : "libpython3.dylib";

        throw new PlatformNotSupportedException("Unsupported OS platform");
    }

    public static string ResolveModulePath(string? hint = null)
    {
        var paths = new List<string>();

        if (!string.IsNullOrEmpty(hint))
            paths.Add(hint);

        string? envPath = Environment.GetEnvironmentVariable("PYTHONPATH");
        if (!string.IsNullOrEmpty(envPath))
            paths.Add(envPath);

        string projectModulePath = FindProjectModulePath();
        if (Directory.Exists(projectModulePath))
            paths.Add(projectModulePath);

        string outputModulePath = GetOutputModulePath();
        if (Directory.Exists(outputModulePath) && outputModulePath != projectModulePath)
            paths.Add(outputModulePath);

        string home = ResolveHome(hint);
        string libPath = Path.Combine(home, "Lib");
        if (Directory.Exists(libPath))
            paths.Add(libPath);

        string sitePackagesPath = Path.Combine(home, "Lib", "site-packages");
        if (Directory.Exists(sitePackagesPath))
            paths.Add(sitePackagesPath);

        string dllsPath = Path.Combine(home, "DLLs");
        if (Directory.Exists(dllsPath))
            paths.Insert(0, dllsPath);

        return string.Join(Path.PathSeparator.ToString(), paths);
    }

    private static string? DiscoverViaPyLauncher()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "py",
                Arguments = "-0",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0) return null;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var match = System.Text.RegularExpressions.Regex.Match(lines[i].Trim(), @"^(-?\d+\.?\d*)\b");
                if (!match.Success) continue;

                var version = match.Groups[1].Value;
                var infoStart = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = $"-{version} -c \"import sys; print(sys.exec_prefix)\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var infoProcess = System.Diagnostics.Process.Start(infoStart);
                if (infoProcess == null) continue;

                var path = infoProcess.StandardOutput.ReadToEnd().Trim();
                infoProcess.WaitForExit(5000);
                if (infoProcess.ExitCode == 0 && !string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? DiscoverViaRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            using var coreKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Python\PythonCore");

            if (coreKey == null)
            {
                using var userKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Python\PythonCore");
                return userKey == null ? null : ScanRegistrySubKeys(userKey);
            }

            return ScanRegistrySubKeys(coreKey);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ScanRegistrySubKeys(Microsoft.Win32.RegistryKey parentKey)
    {
        var versionNames = parentKey.GetSubKeyNames()
            .Where(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+\.\d+$"))
            .OrderDescending()
            .ToArray();

        foreach (var version in versionNames)
        {
            using var installPathKey = parentKey.OpenSubKey($"{version}\\InstallPath");
            if (installPathKey == null) continue;

            var path = installPathKey.GetValue(null) as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;
        }

        return null;
    }

    private static string? DiscoverViaCommonPaths()
    {
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Programs", "Python"),
            @"C:\Program Files\Python",
            @"C:\Program Files (x86)\Python",
            @"C:\Python",
            @"C:\Python3"
        };

        foreach (var basePath in commonPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            var versionDirs = Directory.GetDirectories(basePath, "Python*")
                .OrderByDescending(x => x)
                .ToArray();

            foreach (var dir in versionDirs)
            {
                if (Directory.GetFiles(dir, "python*.dll")
                        .Any(f => Path.GetFileName(f) != "python3.dll" && Path.GetFileName(f).StartsWith("python", StringComparison.OrdinalIgnoreCase)) ||
                    File.Exists(Path.Combine(dir, "python3.dll")))
                    return dir;
            }
        }

        return null;
    }

    private static string? DiscoverViaPath()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = "python",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            var pythonExe = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            var dir = Path.GetDirectoryName(pythonExe);
            if (dir == null) return null;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent != null && HasPythonDll(parent))
                return parent;

            return dir;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasPythonDll(string homePath)
    {
        if (!Directory.Exists(homePath)) return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var versionDlls = Directory.GetFiles(homePath, "python*.dll")
                .Where(f => Path.GetFileName(f) != "python3.dll" && Path.GetFileName(f).StartsWith("python", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (versionDlls.Length > 0) return true;

            return File.Exists(Path.Combine(homePath, "python3.dll"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return File.Exists(Path.Combine(homePath, "libpython3.so"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return File.Exists(Path.Combine(homePath, "libpython3.dylib"));

        return false;
    }

    private static string FindProjectModulePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "pythonmodel");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "__init__.py")))
                return candidate;
            dir = dir.Parent;
        }

        return Path.Combine(baseDir, "PythonModel");
    }

    private static string GetOutputModulePath()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "PythonModel");
    }

    private static string GetEmbeddedHome()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "python_env", "env");
    }
}
