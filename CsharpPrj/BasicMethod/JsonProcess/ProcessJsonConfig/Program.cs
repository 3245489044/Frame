using JsonUIBeautifier.Models;
using JsonBeautifier = JsonUIBeautifier.JsonUIBeautifier;

string jsonConfigDir;
try
{
    jsonConfigDir = PathManager.FileDrivenPaths.GetJsonConfigDir();
}
catch
{
    jsonConfigDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "FileDriven", "DrivenConfig", "JsonConfig"));
}

Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  JsonUIBeautifier - 批量处理 JsonConfig");
Console.WriteLine($"  目录: {jsonConfigDir}");
Console.WriteLine("═══════════════════════════════════════════\n");

var config = new BeautifierConfig
{
    GridAlignment = 0.05,
    EnableThemeExtraction = true,
    EnableStyleDeduplication = true,
    EnableLayoutOptimization = true,
    EnableColorHarmonization = true,
    EnableSpacingNormalization = true,
    EnableLayoutStrategyInference = true
};

var beautifier = new JsonBeautifier(config);

var jsonFiles = Directory.GetFiles(jsonConfigDir, "*.json");
Console.WriteLine($"找到 {jsonFiles.Length} 个 JSON 文件\n");

foreach (var filePath in jsonFiles)
{
    var fileName = Path.GetFileName(filePath);
    Console.WriteLine($"────────────────────────────────────────");
    Console.WriteLine($"  处理: {fileName}");
    Console.WriteLine($"────────────────────────────────────────");

    try
    {
        var report = beautifier.AnalyzeFile(filePath);
        Console.WriteLine($"  📊 分析评分:");
        Console.WriteLine($"     布局质量:     {report.Score.LayoutQuality}/10");
        Console.WriteLine($"     颜色一致性:   {report.Score.ColorConsistency}/10");
        Console.WriteLine($"     间距一致性:   {report.Score.SpacingConsistency}/10");
        Console.WriteLine($"     结构清晰度:   {report.Score.StructureClarity}/10");
        Console.WriteLine($"     ─────────────────────────");
        Console.WriteLine($"     总分:         {report.Score.Total}/10");

        var result = beautifier.ProcessFile(filePath, filePath);

        Console.WriteLine($"  📈 变更统计:");
        Console.WriteLine($"     控件总数:     {result.Report.Statistics.ControlsCount}");
        Console.WriteLine($"     颜色发现:     {result.Report.Statistics.ColorsFound}");
        Console.WriteLine($"     颜色统一:     {result.Report.Statistics.ColorsUnified}");
        Console.WriteLine($"     布局优化:     {result.Report.Statistics.LayoutsOptimized}");
        Console.WriteLine($"     间距标准化:   {result.Report.Statistics.SpacingsNormalized}");
        Console.WriteLine($"     主题提取:     {result.Report.Statistics.ThemesExtracted}");
        Console.WriteLine($"     样式去重:     {result.Report.Statistics.StylesDeduplicated}");
        Console.WriteLine($"     策略推断:     {result.Report.Statistics.StrategiesInferred}");

        if (result.Report.Suggestions.Count > 0)
        {
            Console.WriteLine($"  💡 建议:");
            foreach (var suggestion in result.Report.Suggestions)
                Console.WriteLine($"     • {suggestion}");
        }

        if (result.Report.Changes.Count > 0)
        {
            Console.WriteLine($"  📝 变更记录 ({result.Report.Changes.Count} 项):");
            foreach (var change in result.Report.Changes.Take(15))
                Console.WriteLine($"     [{change.Type}] {change.Description}");
            if (result.Report.Changes.Count > 15)
                Console.WriteLine($"     ... 还有 {result.Report.Changes.Count - 15} 项变更");
        }

        Console.WriteLine($"  ✅ 处理完成，已覆盖源文件\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ❌ 处理失败: {ex.Message}\n");
    }
}

Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  全部处理完成！");
Console.WriteLine("═══════════════════════════════════════════");
