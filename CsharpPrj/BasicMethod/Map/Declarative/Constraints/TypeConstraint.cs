
#nullable enable
using System;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Constraints
{
    public class TypeConstraint : ConstraintBase<object>
    {
        private readonly Type _expectedType;
        private readonly bool _exactMatch;

        public TypeConstraint(
            string name,
            string description,
            Type expectedType,
            bool exactMatch = false)
            : base(name, description)
        {
            _expectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
            _exactMatch = exactMatch;
        }

        public override ConstraintResult Check(object input)
        {
            if (input == null)
                return ConstraintResult.Failed(Name, Description, "值为 null", input);

            var actualType = input.GetType();
            bool isMatch;

            if (_exactMatch)
            {
                isMatch = actualType == _expectedType;
            }
            else
            {
                isMatch = _expectedType.IsAssignableFrom(actualType);
            }

            if (isMatch)
                return ConstraintResult.Satisfied(Name, Description, input);

            var matchMode = _exactMatch ? "精确匹配" : "兼容";
            return ConstraintResult.Failed(
                Name,
                Description,
                $"期望类型 {_expectedType.Name}（{matchMode}），实际类型 {actualType.Name}",
                input);
        }
    }
}
