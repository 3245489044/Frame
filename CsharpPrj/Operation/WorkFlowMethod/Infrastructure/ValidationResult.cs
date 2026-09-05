
using System;
using System.Collections.Generic;

namespace WorkFlowMethod.Infrastructure
{
    public class ValidationResult
    {
        private readonly List<string> _errors = new();

        public bool IsValid => _errors.Count == 0;

        public IReadOnlyList<string> Errors => _errors;

        public void AddError(string error) => _errors.Add(error);

        public string GetErrorString() => string.Join(Environment.NewLine, _errors);

        public static ValidationResult Success() => new();

        public static ValidationResult Failure(string error)
        {
            var result = new ValidationResult();
            result.AddError(error);
            return result;
        }
    }
}
