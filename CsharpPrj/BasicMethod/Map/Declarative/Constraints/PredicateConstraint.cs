
#nullable enable
using System;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Constraints
{
    public class PredicateConstraint<T> : ConstraintBase<T>
    {
        private readonly Func<T, bool> _predicate;
        private readonly Func<T, string>? _failureReasonFactory;

        public PredicateConstraint(
            string name,
            string description,
            Func<T, bool> predicate,
            Func<T, string>? failureReasonFactory = null)
            : base(name, description)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _failureReasonFactory = failureReasonFactory;
        }

        public override ConstraintResult Check(T input)
        {
            if (_predicate(input))
                return ConstraintResult.Satisfied(Name, Description, input);

            var reason = _failureReasonFactory?.Invoke(input)
                ?? $"不满足约束 '{Name}': {Description}";
            return ConstraintResult.Failed(Name, Description, reason, input);
        }
    }
}
