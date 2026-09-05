
#nullable enable
using System;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Constraints
{
    public class RangeConstraint<T> : ConstraintBase<T>
        where T : IComparable<T>
    {
        private readonly T _min;
        private readonly T _max;
        private readonly bool _includeMin;
        private readonly bool _includeMax;

        public RangeConstraint(
            string name,
            string description,
            T min,
            T max,
            bool includeMin = true,
            bool includeMax = true)
            : base(name, description)
        {
            _min = min;
            _max = max;
            _includeMin = includeMin;
            _includeMax = includeMax;

            if (min.CompareTo(max) > 0)
                throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}");
        }

        public override ConstraintResult Check(T input)
        {
            var greaterThanMin = _includeMin
                ? input.CompareTo(_min) >= 0
                : input.CompareTo(_min) > 0;

            var lessThanMax = _includeMax
                ? input.CompareTo(_max) <= 0
                : input.CompareTo(_max) < 0;

            if (greaterThanMin && lessThanMax)
                return ConstraintResult.Satisfied(Name, Description, input);

            var rangeStr = _includeMin ? "[" : "(";
            rangeStr += $"{_min}, {_max}";
            rangeStr += _includeMax ? "]" : ")";

            return ConstraintResult.Failed(
                Name,
                Description,
                $"值 {input} 不在范围 {rangeStr} 内",
                input);
        }
    }
}
