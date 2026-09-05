
namespace UniversalWorkflowFramework.Core.Diagnostics
{
    public interface ICheckRule<TItem>
    {
        string Name { get; }

        string Description { get; }

        void Execute(IEnumerable<TItem> items, DiagnosticResult result);
    }

    public class DiagnosticResult
    {
        public bool HasIssues { get; set; }

        public List<string> Errors { get; set; } = new();

        public List<string> Warnings { get; set; } = new();

        public List<string> Info { get; set; } = new();

        public int TotalItems { get; set; }

        public int TotalReferences { get; set; }

        public int OrphanedReferences { get; set; }

        public int UnusedReferences { get; set; }

        public int CircularReferences { get; set; }

        public int IssueCount => Errors.Count + Warnings.Count;

        public void AddError(string message)
        {
            Errors.Add(message);
            HasIssues = true;
        }

        public void AddWarning(string message)
        {
            Warnings.Add(message);
            HasIssues = true;
        }

        public void AddInfo(string message)
        {
            Info.Add(message);
        }

        public void Merge(DiagnosticResult other)
        {
            if (other == null) return;

            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
            Info.AddRange(other.Info);
            TotalItems += other.TotalItems;
            if (other.HasIssues) HasIssues = true;
        }

        public string GenerateReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 诊断报告 ===");
            sb.AppendLine($"检查项总数: {TotalItems}");
            sb.AppendLine($"问题数: {IssueCount}");

            if (TotalReferences > 0)
            {
                sb.AppendLine($"引用总数: {TotalReferences}");
                sb.AppendLine($"孤立引用: {OrphanedReferences}");
                sb.AppendLine($"未使用引用: {UnusedReferences}");
                sb.AppendLine($"循环引用: {CircularReferences}");
            }

            if (Errors.Count > 0)
            {
                sb.AppendLine($"\n错误 ({Errors.Count}):");
                foreach (var e in Errors) sb.AppendLine($"  ❌ {e}");
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine($"\n警告 ({Warnings.Count}):");
                foreach (var w in Warnings) sb.AppendLine($"  ⚠️ {w}");
            }
            if (Info.Count > 0)
            {
                sb.AppendLine($"\n信息 ({Info.Count}):");
                foreach (var i in Info) sb.AppendLine($"  ℹ️ {i}");
            }
            sb.AppendLine($"\n总体状态: {(HasIssues ? "⚠️ 存在问题" : "✅ 一切正常")}");
            return sb.ToString();
        }
    }
}
