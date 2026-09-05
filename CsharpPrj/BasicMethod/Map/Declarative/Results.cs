
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative
{

    public class ConstraintResult
    {
        public bool IsSatisfied { get; }

        public string ConstraintName { get; }

        public string Description { get; }

        public string? FailureReason { get; }

        public object? CheckedValue { get; }

        private ConstraintResult(
            bool isSatisfied,
            string constraintName,
            string description,
            string? failureReason = null,
            object? checkedValue = null)
        {
            IsSatisfied = isSatisfied;
            ConstraintName = constraintName;
            Description = description;
            FailureReason = failureReason;
            CheckedValue = checkedValue;
        }

        public static ConstraintResult Satisfied(string name, string description, object? value = null)
            => new(true, name, description, checkedValue: value);

        public static ConstraintResult Failed(string name, string description, string reason, object? value = null)
            => new(false, name, description, reason, value);

        public override string ToString()
            => IsSatisfied
                ? $"[✓] {ConstraintName}: {Description}"
                : $"[✗] {ConstraintName}: {Description} — {FailureReason}";
    }


    public class ValidationResult
    {
        public bool IsValid { get; }

        public string TargetName { get; }

        public IReadOnlyList<ConstraintResult> ConstraintResults { get; }

        public int PassedCount { get; }

        public int FailedCount { get; }

        public IReadOnlyList<string> FailureReasons { get; }

        public ValidationResult(string targetName, IEnumerable<ConstraintResult> results)
        {
            TargetName = targetName;
            ConstraintResults = results.ToList().AsReadOnly();
            PassedCount = ConstraintResults.Count(r => r.IsSatisfied);
            FailedCount = ConstraintResults.Count(r => !r.IsSatisfied);
            IsValid = FailedCount == 0;
            FailureReasons = ConstraintResults
                .Where(r => !r.IsSatisfied && r.FailureReason != null)
                .Select(r => r.FailureReason!)
                .ToList()
                .AsReadOnly();
        }

        public override string ToString()
            => IsValid
                ? $"[✓] {TargetName}: 全部 {PassedCount}/{PassedCount + FailedCount} 约束通过"
                : $"[✗] {TargetName}: {FailedCount}/{PassedCount + FailedCount} 约束失败\n"
                  + string.Join("\n", FailureReasons.Select(r => $"  - {r}"));
    }


    public class ResolutionResult<T>
    {
        public bool IsSuccess { get; }

        public T? Value { get; }

        public string? ErrorMessage { get; }

        public Exception? Exception { get; }

        private ResolutionResult(T value)
        {
            IsSuccess = true;
            Value = value;
            ErrorMessage = null;
            Exception = null;
        }

        private ResolutionResult(string error, Exception? exception = null)
        {
            IsSuccess = false;
            Value = default;
            ErrorMessage = error;
            Exception = exception;
        }

        public static ResolutionResult<T> Success(T value) => new(value);

        public static ResolutionResult<T> Failure(string error, Exception? exception = null)
            => new(error, exception);

        public T GetValueOrThrow()
        {
            if (!IsSuccess)
                throw new InvalidOperationException(
                    $"解析失败: {ErrorMessage}", Exception);
            return Value!;
        }

        public T? GetValueOrDefault(T? defaultValue = default)
            => IsSuccess ? Value : defaultValue;

        public override string ToString()
            => IsSuccess
                ? $"[✓] 解析成功: {Value}"
                : $"[✗] 解析失败: {ErrorMessage}";
    }


    public class MappingResult<TRuntime>
    {
        public bool IsSuccess { get; }

        public TRuntime? Value { get; }

        public string? ErrorMessage { get; }

        public string MapperName { get; }

        public string DeclarationName { get; }

        private MappingResult(TRuntime value, string mapperName, string declarationName)
        {
            IsSuccess = true;
            Value = value;
            ErrorMessage = null;
            MapperName = mapperName;
            DeclarationName = declarationName;
        }

        private MappingResult(string error, string mapperName, string declarationName)
        {
            IsSuccess = false;
            Value = default;
            ErrorMessage = error;
            MapperName = mapperName;
            DeclarationName = declarationName;
        }

        public static MappingResult<TRuntime> Success(
            TRuntime value, string mapperName, string declarationName)
            => new(value, mapperName, declarationName);

        public static MappingResult<TRuntime> Failure(
            string error, string mapperName, string declarationName)
            => new(error, mapperName, declarationName);

        public override string ToString()
            => IsSuccess
                ? $"[✓] {MapperName}: {DeclarationName} → {Value}"
                : $"[✗] {MapperName}: {DeclarationName} → {ErrorMessage}";
    }
}
