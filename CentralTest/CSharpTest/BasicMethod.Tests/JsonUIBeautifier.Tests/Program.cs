using JsonUIBeautifier.Models;
using JsonUIBeautifier.Tools;
using JsonBeautifier = JsonUIBeautifier.JsonUIBeautifier;

var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JsonUIBeautifier"));
Directory.SetCurrentDirectory(baseDir);

Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("  测试 1: 基本美化管线");
Console.WriteLine("═══════════════════════════════════════");

var beautifier = new JsonBeautifier();
var result = beautifier.ProcessFile("test_sample.json", "test_sample.beautified.json");

Console.WriteLine($"\n📊 评分:");
Console.WriteLine($"  布局质量:     {result.Report.Score.LayoutQuality}/10");
Console.WriteLine($"  颜色一致性:   {result.Report.Score.ColorConsistency}/10");
Console.WriteLine($"  间距一致性:   {result.Report.Score.SpacingConsistency}/10");
Console.WriteLine($"  结构清晰度:   {result.Report.Score.StructureClarity}/10");
Console.WriteLine($"  ─────────────────────────");
Console.WriteLine($"  总分:         {result.Report.Score.Total}/10");

Console.WriteLine($"\n📈 统计:");
Console.WriteLine($"  控件总数:     {result.Report.Statistics.ControlsCount}");
Console.WriteLine($"  颜色发现:     {result.Report.Statistics.ColorsFound}");
Console.WriteLine($"  颜色统一:     {result.Report.Statistics.ColorsUnified}");
Console.WriteLine($"  布局优化:     {result.Report.Statistics.LayoutsOptimized}");
Console.WriteLine($"  间距标准化:   {result.Report.Statistics.SpacingsNormalized}");
Console.WriteLine($"  主题提取:     {result.Report.Statistics.ThemesExtracted}");
Console.WriteLine($"  样式去重:     {result.Report.Statistics.StylesDeduplicated}");
Console.WriteLine($"  策略推断:     {result.Report.Statistics.StrategiesInferred}");

Console.WriteLine($"\n💡 建议:");
foreach (var suggestion in result.Report.Suggestions)
    Console.WriteLine($"  • {suggestion}");

Console.WriteLine($"\n📝 变更记录 ({result.Report.Changes.Count} 项):");
foreach (var change in result.Report.Changes.Take(10))
    Console.WriteLine($"  [{change.Type}] {change.Description}");

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine("  测试 2: 仅分析模式");
Console.WriteLine("═══════════════════════════════════════");

var report = beautifier.AnalyzeFile("test_sample.json");
Console.WriteLine($"\n📊 分析评分: {report.Score.Total}/10");

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine("  测试 3: 配置验证");
Console.WriteLine("═══════════════════════════════════════");

var validation = beautifier.ValidateFile("test_sample.json");
Console.WriteLine($"\n✅ 有效: {validation.IsValid}");
foreach (var warning in validation.Warnings)
    Console.WriteLine($"  ⚠ {warning}");
foreach (var error in validation.Errors)
    Console.WriteLine($"  ❌ {error}");

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine("  测试 4: JSON 压缩/格式化");
Console.WriteLine("═══════════════════════════════════════");

var json = File.ReadAllText("test_sample.json");
var minified = beautifier.Minify(json);
var pretty = beautifier.PrettyPrint(minified);
Console.WriteLine($"\n  原始大小: {json.Length} 字符");
Console.WriteLine($"  压缩后:   {minified.Length} 字符");
Console.WriteLine($"  压缩率:   {(double)minified.Length / json.Length:P1}");

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine("  测试 5: 自定义配置");
Console.WriteLine("═══════════════════════════════════════");

var customConfig = new BeautifierConfig
{
    GridAlignment = 0.05,
    PrimaryColor = "#2563EB",
    EnableThemeExtraction = true,
    EnableStyleDeduplication = true,
    EnableLayoutOptimization = true,
    EnableColorHarmonization = true,
    EnableSpacingNormalization = true,
    EnableLayoutStrategyInference = true
};

var customBeautifier = new JsonBeautifier(customConfig);
var customResult = customBeautifier.ProcessFile("test_sample.json", "test_sample.custom.json");
Console.WriteLine($"\n📊 自定义配置评分: {customResult.Report.Score.Total}/10");
Console.WriteLine($"  变更项数: {customResult.Report.Changes.Count}");

Console.WriteLine("\n✅ 所有测试完成！");
