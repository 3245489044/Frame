namespace CodeRLFramework.Input
{
    public class CodebaseInput
    {
        public List<string> SourceFiles { get; set; } = new();

        public Dictionary<string, string> FileContents { get; set; } = new();

        public double Weight { get; set; } = 0.7;

        public bool Recursive { get; set; } = true;

        public string FilePattern { get; set; } = "*.cs";

        public void LoadFromFiles()
        {
            FileContents.Clear();
            foreach (var filePath in SourceFiles)
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath);
                    FileContents[filePath] = content;
                }
            }
        }

        public void LoadFromDirectory(string directoryPath)
        {
            var searchOption = Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(directoryPath, FilePattern, searchOption);
            SourceFiles.AddRange(files);
            LoadFromFiles();
        }

        public List<string> GetAllCode()
        {
            return FileContents.Values.ToList();
        }
    }
}
