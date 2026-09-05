using System;
using System.IO;
using PathManager;
using PathManagerAlias = PathManager;

namespace CentralConsole
{
    public static class PathManager
    {
        private static readonly string _baseDirectory = AppContext.BaseDirectory;
        private static readonly string _workspaceRoot = ResolveWorkspaceRoot();

        public static string BaseDirectory => _baseDirectory;
        
        public static string WorkspaceRoot => _workspaceRoot;

        public static string ConfigFilePath => Path.Combine(_baseDirectory, "programs.json");

        public static string GetFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentNullException(nameof(relativePath));

            return Path.IsPathRooted(relativePath) 
                ? relativePath 
                : Path.Combine(_workspaceRoot, relativePath);
        }

        public static string GetWorkingDirectory(string projectPath, string? workingDirectory)
        {
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                var resolvedPath = Path.IsPathRooted(workingDirectory) 
                    ? workingDirectory 
                    : Path.Combine(_workspaceRoot, workingDirectory);
                
                ValidatePath(resolvedPath, nameof(workingDirectory));
                return resolvedPath;
            }

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                throw new InvalidOperationException($"无法从项目路径获取工作目录: {projectPath}");
            }
            
            ValidatePath(projectDir, nameof(projectPath));
            return projectDir;
        }

        public static string GetOutputPath(ProgramInfo programInfo)
        {
            if (string.IsNullOrWhiteSpace(programInfo.WorkingDirectory))
                throw new InvalidOperationException("WorkingDirectory 不能为空");
            
            ValidatePath(programInfo.WorkingDirectory, nameof(programInfo.WorkingDirectory));
            
            var projectName = Path.GetFileName(programInfo.WorkingDirectory);
            var buildOutputDir = ProjectDirectories.GetProjectBuildOutputByName(
                projectName, programInfo.BuildConfiguration);
            var exeName = $"{Path.GetFileNameWithoutExtension(programInfo.ProjectPath)}.exe";
            var buildOutputPath = Path.Combine(buildOutputDir, exeName);
            
            if (File.Exists(buildOutputPath))
                return buildOutputPath;
            
            var outputDir = Path.Combine(programInfo.WorkingDirectory, 
                "bin", programInfo.BuildConfiguration, "net10.0-windows");
            var outputPath = Path.Combine(outputDir, exeName);
            
            ValidatePath(outputDir, "输出目录");
            return outputPath;
        }

        public static string GetLibraryOutputPath(ProgramInfo programInfo)
        {
            if (string.IsNullOrWhiteSpace(programInfo.WorkingDirectory))
                throw new InvalidOperationException("WorkingDirectory 不能为空");
            
            ValidatePath(programInfo.WorkingDirectory, nameof(programInfo.WorkingDirectory));
            
            var projectName = Path.GetFileName(programInfo.WorkingDirectory);
            var dllName = $"{Path.GetFileNameWithoutExtension(programInfo.ProjectPath)}.dll";
            
            var buildOutputDir = ProjectDirectories.GetProjectBuildOutputByName(
                projectName, programInfo.BuildConfiguration);
            var buildOutputPath = Path.Combine(buildOutputDir, dllName);
            
            if (File.Exists(buildOutputPath))
                return buildOutputPath;
            
            var frameworkVersions = new[] { "net10.0-windows", "net10.0-windows7.0", "net10.0" };
            string outputDir = null;
            
            foreach (var framework in frameworkVersions)
            {
                var candidateDir = Path.Combine(programInfo.WorkingDirectory, 
                    "bin", programInfo.BuildConfiguration, framework);
                if (Directory.Exists(candidateDir))
                {
                    outputDir = candidateDir;
                    break;
                }
            }
            
            if (outputDir == null)
            {
                outputDir = Path.Combine(programInfo.WorkingDirectory, 
                    "bin", programInfo.BuildConfiguration, "net10.0-windows");
            }
            
            var outputPath = Path.Combine(outputDir, dllName);
            
            ValidatePath(outputDir, "输出目录");
            return outputPath;
        }

        private static string ResolveWorkspaceRoot()
        {
            try
            {
                var currentDir = _baseDirectory;
                
                var configDir = Path.GetDirectoryName(Path.Combine(_baseDirectory, "programs.json"));
                if (!string.IsNullOrEmpty(configDir) && Directory.Exists(configDir))
                {
                    var parentDir = Path.GetDirectoryName(configDir);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        currentDir = parentDir;
                    }
                }
                
                for (int i = 0; i < 5; i++)
                {
                    var hasUniversalReference = Directory.Exists(Path.Combine(currentDir, "UniversalReference"));
                    var hasWorkFlowMethod = Directory.Exists(Path.Combine(currentDir, "WorkFlowMethod"));
                    var hasCellFrame = Directory.Exists(Path.Combine(currentDir, "CellFrame"));
                    
                    if (hasUniversalReference || hasWorkFlowMethod || hasCellFrame)
                    {
                        return currentDir;
                    }
                    
                    var parentDir = Path.GetDirectoryName(currentDir);
                    if (parentDir == null || IsRootDirectory(parentDir))
                        break;
                    currentDir = parentDir;
                }

                var exeParentDir = Path.GetDirectoryName(_baseDirectory);
                if (!string.IsNullOrEmpty(exeParentDir) && !IsRootDirectory(exeParentDir))
                {
                    return exeParentDir;
                }

                return _baseDirectory;
            }
            catch
            {
                return _baseDirectory;
            }
        }

        private static bool IsRootDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            try
            {
                var root = Path.GetPathRoot(path);
                return string.Equals(root, path, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private static void ValidatePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(parameterName);

            if (IsRootDirectory(path))
                throw new InvalidOperationException($"路径不能是根目录: {path}");

            try
            {
                Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无效的路径: {path}", ex);
            }
        }

        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                return !IsRootDirectory(fullPath);
            }
            catch
            {
                return false;
            }
        }

        public static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}