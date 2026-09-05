
#nullable enable
using System;
using System.Collections;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Constraints
{
    public class RequiredConstraint : ConstraintBase<object>
    {
        private readonly bool _checkEmptyString;
        private readonly bool _checkEmptyCollection;

        public RequiredConstraint(
            string name,
            string description,
            bool checkEmptyString = true,
            bool checkEmptyCollection = true)
            : base(name, description)
        {
            _checkEmptyString = checkEmptyString;
            _checkEmptyCollection = checkEmptyCollection;
        }

        public override ConstraintResult Check(object input)
        {
            if (input == null)
                return ConstraintResult.Failed(Name, Description, "值为 null", input);

            if (_checkEmptyString && input is string str && string.IsNullOrWhiteSpace(str))
                return ConstraintResult.Failed(Name, Description, "值为空字符串", input);

            if (_checkEmptyCollection && input is ICollection collection && collection.Count == 0)
                return ConstraintResult.Failed(Name, Description, "值为空集合", input);

            return ConstraintResult.Satisfied(Name, Description, input);
        }
    }
}
