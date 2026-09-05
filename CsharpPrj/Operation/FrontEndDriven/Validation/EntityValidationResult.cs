
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ECS.Core;

namespace FrontEndDriven.Validation;

public class ValidationError
{
    public Entity EntityId { get; }

    public string EntityName { get; }

    public ValidationCategory Category { get; }

    public string Message { get; }

    public string? ComponentType { get; }

    public ValidationSeverity Severity { get; }

    public ValidationError(
        Entity entityId,
        string entityName,
        ValidationCategory category,
        string message,
        ValidationSeverity severity = ValidationSeverity.Error,
        string? componentType = null)
    {
        EntityId = entityId;
        EntityName = entityName;
        Category = category;
        Message = message;
        Severity = severity;
        ComponentType = componentType;
    }

    public override string ToString()
    {
        var prefix = Severity == ValidationSeverity.Error ? "ERR" : "WRN";
        var comp = ComponentType != null ? $"[{ComponentType}]" : "";
        return $"[{prefix}][{Category}] Entity={EntityName}{comp}: {Message}";
    }
}

public class ValidationResult
{
    public List<ValidationError> Errors { get; } = new();

    public List<ValidationError> Warnings { get; } = new();

    public int TotalChecked { get; set; }

    [Obsolete("PassedCount 语义不明确（TotalChecked 是 Entity 数，TotalIssues 是问题数，一个 Entity 可能有多个问题）。" +
              "请直接使用 TotalChecked、Errors.Count、Warnings.Count 进行统计。")]
    public int PassedCount => Math.Max(0, TotalChecked - TotalIssues);

    public bool IsValid => Errors.Count == 0;

    public bool HasWarnings => Warnings.Count > 0;

    public int TotalIssues => Errors.Count + Warnings.Count;

    public void AddError(ValidationError error)
    {
        Errors.Add(error);
    }

    public void AddWarning(ValidationError warning)
    {
        Warnings.Add(warning);
    }

    public List<ValidationError> GetErrorsByCategory(ValidationCategory category)
    {
        return Errors.Where(e => e.Category == category).ToList();
    }

    public List<ValidationError> GetWarningsByCategory(ValidationCategory category)
    {
        return Warnings.Where(e => e.Category == category).ToList();
    }

    public string GetSummary()
    {
        return $"校验完成: 检查={TotalChecked}, " +
               $"错误={Errors.Count}, 警告={Warnings.Count}, " +
               $"状态={(IsValid ? "通过" : "未通过")}";
    }

    public string GetDetailText()
    {
        var lines = new List<string> { GetSummary() };

        if (Errors.Count > 0)
        {
            lines.Add("--- 错误 ---");
            lines.AddRange(Errors.Select(e => "  " + e.ToString()));
        }

        if (Warnings.Count > 0)
        {
            lines.Add("--- 警告 ---");
            lines.AddRange(Warnings.Select(w => "  " + w.ToString()));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
