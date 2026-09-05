using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace CodePathExplorer.Scanner;

public class SolutionProjectScanner
{
    public SolutionScanResult Scan(string path)
    {
        if (File.Exists(path))
        {
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return ScanSolutionFile(path);
            }

            if (path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return ScanSolutionXmlFile(path);
            }

            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return ScanSingleProject(path);
            }
        }

        if (Directory.Exists(path))
        {
            var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                return ScanSolutionFile(slnFiles[0]);
            }

            var slnxFiles = Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly);
            if (slnxFiles.Length > 0)
            {
                return ScanSolutionXmlFile(slnxFiles[0]);
            }

            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
            {
                return ScanMultipleProjects(csprojFiles);
            }

            return new SolutionScanResult
            {
                IsSolutionMode = false,
                Projects = new List<ProjectInfo>()
            };
        }

        throw new DirectoryNotFoundException($"路径不存在: {path}");
    }

    private SolutionScanResult ScanSolutionFile(string slnPath)
    {
        var slnDir = Path.GetDirectoryName(Path.GetFullPath(slnPath))!;
        var content = File.ReadAllText(slnPath);
        var projectPaths = new List<string>();

        var lines = content.Split('\n', '\r');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Project("))
            {
                var parts = trimmed.Split(',');
                if (parts.Length >= 2)
                {
                    var projectPath = parts[1].Trim().Trim('"').Replace("\\", "/");
                    if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var fullPath = Path.GetFullPath(Path.Combine(slnDir, projectPath));
                        if (File.Exists(fullPath))
                        {
                            projectPaths.Add(fullPath);
                        }
                    }
                }
            }
        }

        var projects = projectPaths.Select(ParseProjectFile).Where(p => p != null).Select(p => p!).ToList();

        ResolveProjectReferences(projects);

        var orderedProjects = TopologicalSort(projects);
        var projectContexts = CompileProjects(orderedProjects);

        return new SolutionScanResult
        {
            IsSolutionMode = true,
            SolutionPath = slnPath,
            Projects = orderedProjects,
            ProjectContexts = projectContexts
        };
    }

    private SolutionScanResult ScanSolutionXmlFile(string slnxPath)
    {
        var slnxDir = Path.GetDirectoryName(Path.GetFullPath(slnxPath))!;
        var doc = XDocument.Load(slnxPath);
        var projectPaths = new List<string>();

        foreach (var projectElem in doc.Descendants("Project"))
        {
            var projectPath = projectElem.Attribute("Path")?.Value;
            if (!string.IsNullOrEmpty(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(Path.Combine(slnxDir, projectPath.Replace("/", "\\")));
                if (File.Exists(fullPath))
                {
                    projectPaths.Add(fullPath);
                }
            }
        }

        var projects = projectPaths.Select(ParseProjectFile).Where(p => p != null).Select(p => p!).ToList();

        ResolveProjectReferences(projects);

        var orderedProjects = TopologicalSort(projects);
        var projectContexts = CompileProjects(orderedProjects);

        return new SolutionScanResult
        {
            IsSolutionMode = true,
            SolutionPath = slnxPath,
            Projects = orderedProjects,
            ProjectContexts = projectContexts
        };
    }

    private SolutionScanResult ScanSingleProject(string csprojPath)
    {
        var project = ParseProjectFile(csprojPath);
        if (project == null)
        {
            return new SolutionScanResult { IsSolutionMode = false };
        }

        var projects = new List<ProjectInfo> { project };
        var projectContexts = CompileProjects(projects);

        return new SolutionScanResult
        {
            IsSolutionMode = true,
            Projects = projects,
            ProjectContexts = projectContexts
        };
    }

    private SolutionScanResult ScanMultipleProjects(string[] csprojPaths)
    {
        var projects = csprojPaths
            .Select(ParseProjectFile)
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        ResolveProjectReferences(projects);
        var orderedProjects = TopologicalSort(projects);
        var projectContexts = CompileProjects(orderedProjects);

        return new SolutionScanResult
        {
            IsSolutionMode = true,
            Projects = orderedProjects,
            ProjectContexts = projectContexts
        };
    }

    private ProjectInfo? ParseProjectFile(string csprojPath)
    {
        try
        {
            var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var projectName = Path.GetFileNameWithoutExtension(csprojPath);

            var targetFramework = doc.Descendants(ns + "TargetFramework")
                .FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "TargetFrameworks")
                    .FirstOrDefault()?.Value?.Split(';').FirstOrDefault()
                ?? "net10.0";

            var outputType = doc.Descendants(ns + "OutputType")
                .FirstOrDefault()?.Value ?? "Library";

            var sourceFiles = Directory.GetFiles(csprojDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();

            var projectReferences = doc.Descendants(ns + "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.GetFullPath(Path.Combine(csprojDir, r!)))
                .ToList();

            var packageReferences = doc.Descendants(ns + "PackageReference")
                .Select(e => new NuGetPackageInfo
                {
                    Name = e.Attribute("Include")?.Value ?? "",
                    Version = e.Attribute("Version")?.Value ?? ""
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();

            return new ProjectInfo
            {
                Name = projectName,
                CsprojPath = csprojPath,
                ProjectDirectory = csprojDir,
                TargetFramework = targetFramework,
                OutputType = outputType,
                SourceFiles = sourceFiles,
                ProjectReferencePaths = projectReferences,
                PackageReferences = packageReferences
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  警告: 解析项目文件失败: {csprojPath} - {ex.Message}");
            return null;
        }
    }

    private void ResolveProjectReferences(List<ProjectInfo> projects)
    {
        var projectMap = projects
            .Where(p => p != null)
            .ToDictionary(p => Path.GetFullPath(p.CsprojPath), StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            foreach (var refPath in project.ProjectReferencePaths)
            {
                var fullRefPath = Path.GetFullPath(refPath);
                if (projectMap.TryGetValue(fullRefPath, out var referencedProject))
                {
                    project.ProjectReferences.Add(referencedProject);
                }
            }
        }
    }

    private List<ProjectInfo> TopologicalSort(List<ProjectInfo> projects)
    {
        var sorted = new List<ProjectInfo>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(ProjectInfo project)
        {
            var path = Path.GetFullPath(project.CsprojPath);
            if (visited.Contains(path)) return;
            if (visiting.Contains(path))
            {
                Console.WriteLine($"  警告: 检测到循环引用: {project.Name}");
                return;
            }

            visiting.Add(path);
            foreach (var dep in project.ProjectReferences)
            {
                Visit(dep);
            }
            visiting.Remove(path);
            visited.Add(path);
            sorted.Add(project);
        }

        foreach (var project in projects)
        {
            Visit(project);
        }

        return sorted;
    }

    private List<ProjectCompilationContext> CompileProjects(List<ProjectInfo> projects)
    {
        var compiledProjects = new Dictionary<string, Compilation>(StringComparer.OrdinalIgnoreCase);
        var contexts = new List<ProjectCompilationContext>();

        foreach (var project in projects)
        {
            try
            {
                var syntaxTrees = new List<SyntaxTree>();
                var sourceCodeMap = new Dictionary<string, (SyntaxTree Tree, string Code)>(StringComparer.OrdinalIgnoreCase);

                foreach (var sourceFile in project.SourceFiles)
                {
                    try
                    {
                        var code = File.ReadAllText(sourceFile);
                        var tree = CSharpSyntaxTree.ParseText(code);
                        syntaxTrees.Add(tree);
                        sourceCodeMap[sourceFile] = (tree, code);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  解析文件失败: {sourceFile} - {ex.Message}");
                    }
                }

                var references = new List<MetadataReference>();

                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(System.Text.StringBuilder).Assembly.Location));

                foreach (var pkg in project.PackageReferences)
                {
                    try
                    {
                        var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name?.Equals(pkg.Name, StringComparison.OrdinalIgnoreCase) == true);
                        if (asm != null && !string.IsNullOrEmpty(asm.Location))
                        {
                            references.Add(MetadataReference.CreateFromFile(asm.Location));
                        }
                    }
                    catch { }
                }

                foreach (var dep in project.ProjectReferences)
                {
                    var depPath = Path.GetFullPath(dep.CsprojPath);
                    if (compiledProjects.TryGetValue(depPath, out var depCompilation))
                    {
                        references.Add(depCompilation.ToMetadataReference());
                    }
                }

                var compilation = CSharpCompilation.Create(project.Name)
                    .AddSyntaxTrees(syntaxTrees)
                    .AddReferences(references)
                    .WithOptions(new CSharpCompilationOptions(
                        project.OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                            ? OutputKind.ConsoleApplication
                            : OutputKind.DynamicallyLinkedLibrary));

                var fileContexts = new List<AnalysisContext>();
                foreach (var sourceFile in project.SourceFiles)
                {
                    try
                    {
                        if (sourceCodeMap.TryGetValue(sourceFile, out var entry))
                        {
                            var semanticModel = compilation.GetSemanticModel(entry.Tree);
                            var root = entry.Tree.GetRoot();

                            var context = new AnalysisContext
                            {
                                FilePath = sourceFile,
                                SourceCode = entry.Code,
                                SyntaxTree = entry.Tree,
                                Root = root,
                                SemanticModel = semanticModel,
                                Compilation = compilation
                            };

                            fileContexts.Add(context);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  创建分析上下文失败: {sourceFile} - {ex.Message}");
                    }
                }

                var diagnostics = compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(10)
                    .Select(d => d.ToString())
                    .ToList();

                var projectContext = new ProjectCompilationContext
                {
                    Project = project,
                    Compilation = compilation,
                    FileContexts = fileContexts,
                    CompilationErrors = diagnostics
                };

                contexts.Add(projectContext);
                compiledProjects[Path.GetFullPath(project.CsprojPath)] = compilation;

                if (diagnostics.Count > 0)
                {
                    Console.WriteLine($"  项目 {project.Name}: {diagnostics.Count} 个编译错误");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  编译项目失败: {project.Name} - {ex.Message}");
            }
        }

        return contexts;
    }
}

public class SolutionScanResult
{
    public bool IsSolutionMode { get; set; }

    public string? SolutionPath { get; set; }

    public List<ProjectInfo> Projects { get; set; } = new();

    public List<ProjectCompilationContext> ProjectContexts { get; set; } = new();
}

public class ProjectInfo
{
    public string Name { get; set; } = "";

    public string CsprojPath { get; set; } = "";

    public string ProjectDirectory { get; set; } = "";

    public string TargetFramework { get; set; } = "net10.0";

    public string OutputType { get; set; } = "Library";

    public List<string> SourceFiles { get; set; } = new();

    public List<string> ProjectReferencePaths { get; set; } = new();

    public List<ProjectInfo> ProjectReferences { get; set; } = new();

    public List<NuGetPackageInfo> PackageReferences { get; set; } = new();
}

public class NuGetPackageInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

public class ProjectCompilationContext
{
    public ProjectInfo Project { get; set; } = new();

    public Compilation Compilation { get; set; } = null!;

    public List<AnalysisContext> FileContexts { get; set; } = new();

    public List<string> CompilationErrors { get; set; } = new();
}
