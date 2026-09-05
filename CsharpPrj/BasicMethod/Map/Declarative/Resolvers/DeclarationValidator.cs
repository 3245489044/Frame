
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Resolvers
{
    public class DeclarationValidator<TDeclaration>
        where TDeclaration : TConfig.Declarative.IDeclaration
    {
        private readonly string _name;
        private readonly List<IConstraint<TDeclaration>> _constraints = new();

        public string Name => _name;

        public int ConstraintCount => _constraints.Count;

        public DeclarationValidator(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public DeclarationValidator<TDeclaration> AddConstraint(IConstraint<TDeclaration> constraint)
        {
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));
            _constraints.Add(constraint);
            return this;
        }

        public DeclarationValidator<TDeclaration> AddConstraints(IEnumerable<IConstraint<TDeclaration>> constraints)
        {
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));
            _constraints.AddRange(constraints);
            return this;
        }

        public ValidationResult Validate(TDeclaration declaration)
        {
            var results = _constraints.Select(c => c.Check(declaration));
            return new ValidationResult(declaration.Name, results);
        }

        public IReadOnlyList<ValidationResult> ValidateAll(IEnumerable<TDeclaration> declarations)
        {
            return declarations.Select(Validate).ToList().AsReadOnly();
        }
    }
}
