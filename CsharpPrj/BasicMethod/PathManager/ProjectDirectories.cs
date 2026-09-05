using System.Text.RegularExpressions;

namespace PathManager;

public static class ProjectDirectories
{
    public static string ProjectRoot => ProjectPathManager.FindProjectRoot();


    private static string? _csharpProjectRoot;

    public static string CsharpProjectRoot
    {
        get
        {
            if (_csharpProjectRoot != null)
                return _csharpProjectRoot;

            var buildProps = Path.Combine(ProjectRoot, "CsharpPrj", "Directory.Build.props");
            if (File.Exists(buildProps))
            {
                try
                {
                    var xml = File.ReadAllText(buildProps);
                    var match = Regex.Match(xml, @"<CsharpProjectRoot>([^<]+)</CsharpProjectRoot>");
                    _csharpProjectRoot = match.Success ? match.Groups[1].Value : "CsharpPrj";
                }
                catch
                {
                    _csharpProjectRoot = "CsharpPrj";
                }
            }
            else
            {
                _csharpProjectRoot = "CsharpPrj";
            }

            return _csharpProjectRoot;
        }
    }

    public static void ResetCsharpProjectRootCache()
    {
        _csharpProjectRoot = null;
    }


    public static string BasicMethod => Combine(CsharpProjectRoot, "BasicMethod");
    public static string BatTool => Combine("BatTool");
    public static string CellFrame => Combine("CellFrame");
    public static string CentralConsole => Combine("CentralConsole");
    public static string DataManage => Combine("DataManage");
    public static string CsharpPrj => Combine(CsharpProjectRoot);
    [Obsolete("FileDriven 已分离为独立项目，请使用 FileDriven.PathMananger 获取路径")]
    public static string FileDriven => Combine(CsharpProjectRoot, "FileDriven");
    public static string FunctionStorage => Combine("FunctionStorage");
    public static string Godot => Combine("Godot");
    public static string JsonProcess => Combine("JsonProcess");
    public static string PythonModel => Combine("PythonModel");
    public static string RoslynMethod => Combine("RoslynMethod");
    public static string Script => Combine("Script");
    public static string Tools => Combine("Tools");
    public static string UniversalReference => Combine("UniversalReference");
    public static string WdUI => Combine("WdUI");
    public static string WorkFlow => Combine("WorkFlow");
    public static string WorkFlowMethod => Combine("WorkFlowMethod");
    public static string Docs => Combine("docs");


    public static string FileDriven_DrivenConfig =>
        Path.Combine(FileDriven, "DrivenConfig");

    public static string FileDriven_JsonConfig =>
        Path.Combine(FileDriven_DrivenConfig, "JsonConfig");

    public static string FileDriven_WorkFlowConfig =>
        Path.Combine(FileDriven_DrivenConfig, "WorkFlow");

    public static string FileDriven_Scripts =>
        Path.Combine(FileDriven_DrivenConfig, "Scripts");

    public static string FileDriven_TempletConfig =>
        Path.Combine(FileDriven_DrivenConfig, "TempletConfig");

    public static string FileDriven_Configs =>
        Path.Combine(FileDriven_DrivenConfig, "Configs");

    public static string FileDriven_Usings =>
        Path.Combine(FileDriven_DrivenConfig, "Usings");

    public static string FileDriven_Launcher =>
        Path.Combine(FileDriven, "FileDriven.Launcher");

    public static string FileDriven_UI =>
        Path.Combine(FileDriven, "FileDriven.UI");

    public static string FileDriven_Core =>
        Path.Combine(FileDriven, "FileDrivenFramework.Core");


    public static string BuildOutput => Path.Combine(ProjectRoot, "BuildOutput");

    public static string GetProjectBuildOutput(string projectDir, string configuration = "Debug", string targetFramework = "net10.0-windows")
    {
        var projectName = Path.GetFileName(projectDir);
        return Path.Combine(BuildOutput, configuration, projectName, targetFramework);
    }

    public static string GetProjectBuildOutputByName(string projectName, string configuration = "Debug", string targetFramework = "net10.0-windows")
    {
        return Path.Combine(BuildOutput, configuration, projectName, targetFramework);
    }


    public static string FileDriven_LauncherOutputDir
    {
        get
        {
            var release = GetProjectBuildOutputByName("FileDriven.Launcher", "Release");
            var debug = GetProjectBuildOutputByName("FileDriven.Launcher", "Debug");

            if (File.Exists(Path.Combine(release, "FileDriven.Launcher.exe")))
                return release;

            return debug;
        }
    }

    public static string FileDriven_LauncherExe =>
        Path.Combine(FileDriven_LauncherOutputDir, "FileDriven.Launcher.exe");


    public static string WorkFlow_BusinessLogic =>
        Path.Combine(WorkFlow, "BusinessLogic");

    public static string WorkFlow_Program =>
        Path.Combine(WorkFlow, "Porgram");


    public static string GetPath(params string[] segments)
    {
        return Path.Combine(ProjectRoot, Path.Combine(segments));
    }

    private static string Combine(string relative)
    {
        return Path.Combine(ProjectRoot, relative);
    }

    private static string Combine(string relative1, string relative2)
    {
        return Path.Combine(ProjectRoot, relative1, relative2);
    }
}
