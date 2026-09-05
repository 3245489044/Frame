using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PathManager;

namespace Workflow.Common
{
    public static class PackageManager
    {
        public static ValidationResult ValidateDependencies()
        {
            var result = new ValidationResult();
            var projectFiles = Directory.GetFiles(
                ProjectDirectories.ProjectRoot,
                "*.csproj",
                SearchOption.AllDirectories);

            foreach (var projectFile in projectFiles)
            {
                ValidateProjectDependencies(projectFile, result);
            }

            return result;
        }

        public static void UpdatePackage(string packageName, string version)
        {
            var directoryBuildPropsPath = Path.Combine(ProjectDirectories.ProjectRoot, "Directory.Build.props");

            if (!File.Exists(directoryBuildPropsPath))
            {
                throw new FileNotFoundException("Directory.Build.props文件未找到");
            }

            var content = File.ReadAllText(directoryBuildPropsPath);
            var propertyName = $"{packageName.Replace(".", "")}Version";
            var pattern = $@"<{propertyName}>(.*?)</{propertyName}>";

            if (Regex.IsMatch(content, pattern))
            {
                content = Regex.Replace(content, pattern, $"<{propertyName}>{version}</{propertyName}>");
                File.WriteAllText(directoryBuildPropsPath, content);
            }
            else
            {
                throw new ArgumentException($"包 {packageName} 未在全局配置中定义");
            }
        }

        public static List<ProjectDependencyReport> GenerateDependencyReport()
        {
            var reports = new List<ProjectDependencyReport>();
            var projectFiles = Directory.GetFiles(
                ProjectDirectories.ProjectRoot,
                "*.csproj",
                SearchOption.AllDirectories);

            foreach (var projectFile in projectFiles)
            {
                var report = AnalyzeProjectDependencies(projectFile);
                reports.Add(report);
            }

            return reports;
        }

        public static List<string> FindDirectPackageReferences()
        {
            var directReferences = new List<string>();
            var projectFiles = Directory.GetFiles(
                ProjectDirectories.ProjectRoot,
                "*.csproj",
                SearchOption.AllDirectories);

            foreach (var projectFile in projectFiles)
            {
                if (IsWorkflowDependenciesProject(projectFile)) continue;

                var content = File.ReadAllText(projectFile);
                var packageReferences = ExtractPackageReferences(content);

                if (packageReferences.Any())
                {
                    directReferences.Add($"{Path.GetFileName(projectFile)}: {string.Join(", ", packageReferences)}");
                }
            }

            return directReferences;
        }

        private static void ValidateProjectDependencies(string projectFile, ValidationResult result)
        {
            var projectName = Path.GetFileName(projectFile);

            if (IsWorkflowDependenciesProject(projectFile)) return;

            var content = File.ReadAllText(projectFile);

            if (!content.Contains("WorkflowDependencies.csproj"))
            {
                result.Errors.Add($"项目 {projectName} 未引用WorkflowDependencies");
            }

            var directReferences = ExtractPackageReferences(content);
            if (directReferences.Any())
            {
                result.Warnings.Add($"项目 {projectName} 存在直接包引用: {string.Join(", ", directReferences)}");
            }
        }

        private static ProjectDependencyReport AnalyzeProjectDependencies(string projectFile)
        {
            var content = File.ReadAllText(projectFile);
            var report = new ProjectDependencyReport
            {
                ProjectName = Path.GetFileName(projectFile),
                ProjectPath = projectFile,
                HasWorkflowDependenciesReference = content.Contains("WorkflowDependencies.csproj"),
                DirectPackageReferences = ExtractPackageReferences(content),
                ProjectReferences = ExtractProjectReferences(content)
            };

            return report;
        }

        private static List<string> ExtractPackageReferences(string projectContent)
        {
            var references = new List<string>();
            var pattern = "PackageReference Include=\"([^\"]+)\" Version=\"([^\"]+)\"";
            var matches = Regex.Matches(projectContent, pattern);

            foreach (Match match in matches)
            {
                references.Add($"{match.Groups[1].Value} ({match.Groups[2].Value})");
            }

            return references;
        }

        private static List<string> ExtractProjectReferences(string projectContent)
        {
            var references = new List<string>();
            var pattern = "ProjectReference Include=\"([^\"]+)\"";
            var matches = Regex.Matches(projectContent, pattern);

            foreach (Match match in matches)
            {
                references.Add(match.Groups[1].Value);
            }

            return references;
        }

        private static bool IsWorkflowDependenciesProject(string projectFile)
        {
            return Path.GetFileName(projectFile).Equals("WorkflowDependencies.csproj", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class ValidationResult
    {
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool IsValid => !Errors.Any();
    }

    public class ProjectDependencyReport
    {
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public bool HasWorkflowDependenciesReference { get; set; }
        public List<string> DirectPackageReferences { get; set; } = new List<string>();
        public List<string> ProjectReferences { get; set; } = new List<string>();
    }
}