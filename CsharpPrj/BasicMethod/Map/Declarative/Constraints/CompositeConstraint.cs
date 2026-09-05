
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Constraints
{
    public enum CompositeMode
    {
        All,
        Any,
        Not
    }

    public class CompositeConstraint<T> : ConstraintBase<T>
    {
        private readonly IReadOnlyList<IConstraint<T>> _constraints;
        private readonly CompositeMode _mode;

        public CompositeConstraint(
            string name,
            string description,
            CompositeMode mode,
            params IConstraint<T>[] constraints)
            : base(name, description)
        {
            if (constraints == null || constraints.Length == 0)
                throw new ArgumentException("至少需要一个子约束", nameof(constraints));

            _constraints = constraints;
            _mode = mode;
        }

        public override ConstraintResult Check(T input)
        {
            switch (_mode)
            {
                case CompositeMode.All:
                    return CheckAll(input);
                case CompositeMode.Any:
                    return CheckAny(input);
                case CompositeMode.Not:
                    return CheckNot(input);
                default:
                    return ConstraintResult.Failed(Name, Description, $"未知的组合模式: {_mode}", input);
            }
        }

        private ConstraintResult CheckAll(T input)
        {
            var failed = new List<string>();
            foreach (var constraint in _constraints)
            {
                var result = constraint.Check(input);
                if (!result.IsSatisfied)
                {
                    failed.Add($"[{constraint.Name}] {result.FailureReason}");
                }
            }

            if (failed.Count == 0)
                return ConstraintResult.Satisfied(Name, Description, input);

            return ConstraintResult.Failed(
                Name,
                Description,
                $"AND 约束失败 ({failed.Count}/{_constraints.Count}):\n"
                + string.Join("\n", failed.Select(f => $"  - {f}")),
                input);
        }

        private ConstraintResult CheckAny(T input)
        {
            foreach (var constraint in _constraints)
            {
                var result = constraint.Check(input);
                if (result.IsSatisfied)
                    return ConstraintResult.Satisfied(Name, Description, input);
            }

            return ConstraintResult.Failed(
                Name,
                Description,
                $"OR 约束失败：所有 {_constraints.Count} 个子约束均不满足",
                input);
        }

        private ConstraintResult CheckNot(T input)
        {
            if (_constraints.Count != 1)
                return ConstraintResult.Failed(
                    Name,
                    Description,
                    $"NOT 模式需要恰好 1 个子约束，当前有 {_constraints.Count} 个",
                    input);

            var result = _constraints[0].Check(input);
            if (!result.IsSatisfied)
                return ConstraintResult.Satisfied(Name, Description, input);

            return ConstraintResult.Failed(
                Name,
                Description,
                $"NOT 约束失败：子约束 '{_constraints[0].Name}' 被满足",
                input);
        }
    }
}
