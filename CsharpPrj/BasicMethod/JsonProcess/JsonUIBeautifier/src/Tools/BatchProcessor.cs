using JsonUIBeautifier.Models;
using JsonUIBeautifier.Pipeline;

namespace JsonUIBeautifier.Tools;

public class BatchProcessor
{
    private readonly JsonBeautifierPipeline _pipeline;

    public BatchProcessor(JsonBeautifierPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public class BatchResult
    {
        public int TotalFiles { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public List<FileResult> Results { get; set; } = new();
    }

    public class FileResult
    {
        public string FilePath { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
        public BeautifyReport? Report { get; set; }
    }

    public BatchResult ProcessDirectory(string directoryPath, string outputSuffix = ".beautified", bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var jsonFiles = Directory.GetFiles(directoryPath, "*.json", searchOption);
        return ProcessFilesInternal(jsonFiles, outputSuffix);
    }

    public BatchResult ProcessFiles(IEnumerable<string> filePaths, string outputSuffix = ".beautified")
    {
        return ProcessFilesInternal(filePaths, outputSuffix);
    }

    private BatchResult ProcessFilesInternal(IEnumerable<string> filePaths, string outputSuffix)
    {
        var result = new BatchResult();
        var files = filePaths.ToList();
        result.TotalFiles = files.Count;

        foreach (var file in files)
        {
            try
            {
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(file)!,
                    Path.GetFileNameWithoutExtension(file) + outputSuffix + ".json");

                var beautifyResult = _pipeline.ProcessFile(file, outputPath);

                result.Results.Add(new FileResult
                {
                    FilePath = file,
                    Success = true,
                    Report = beautifyResult.Report
                });
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.Results.Add(new FileResult
                {
                    FilePath = file,
                    Success = false,
                    Error = ex.Message
                });
                result.FailCount++;
            }
        }

        return result;
    }

}
