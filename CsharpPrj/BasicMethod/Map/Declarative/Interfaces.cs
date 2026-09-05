
#nullable enable
using System;
using System.Collections.Generic;

namespace BasicMethod.Declarative
{

    public interface IDeclaration : TConfig.Declarative.IDeclaration
    {
    }


    public interface IDeclaration<out TTarget> : IDeclaration
    {
        TTarget? Target { get; }
    }


    public interface IConstraint<in T>
    {
        string Name { get; }

        string Description { get; }

        ConstraintResult Check(T input);
    }


    public interface IRelation
    {
        string Name { get; }

        IReadOnlyList<string> Attributes { get; }

        IEnumerable<IReadOnlyDictionary<string, object?>> Tuples { get; }
    }


    public interface IDeclarativeMapper<in TDeclaration, out TRuntime>
        where TDeclaration : TConfig.Declarative.IDeclaration
    {
        string Name { get; }

        string Description { get; }

        TRuntime Map(TDeclaration declaration);
    }


    public interface IAsyncDeclarativeMapper<in TDeclaration, TRuntime>
        where TDeclaration : TConfig.Declarative.IDeclaration
    {
        string Name { get; }

        string Description { get; }

        Task<TRuntime> MapAsync(TDeclaration declaration, CancellationToken cancellationToken = default);
    }


    public interface IDeclarationCollection<TDeclaration> where TDeclaration : TConfig.Declarative.IDeclaration
    {
        IReadOnlyList<TDeclaration> Items { get; }

        int Count { get; }

        IDeclarationCollection<TDeclaration> Register(TDeclaration item);

        IDeclarationCollection<TDeclaration> RegisterRange(IEnumerable<TDeclaration> items);

        TDeclaration? Find(string name);

        IEnumerable<TDeclaration> FindByType(string declarationType);

        IDeclarationCollection<TDeclaration> Remove(string name);

        void Clear();
    }


    public interface IDeclarationResolver<in TSource, TDeclaration>
        where TDeclaration : TConfig.Declarative.IDeclaration
    {
        string Name { get; }

        ResolutionResult<TDeclaration> Resolve(TSource source);

        ResolutionResult<IReadOnlyList<TDeclaration>> ResolveAll(IEnumerable<TSource> sources);
    }


    public interface IDeclarationValidator<TDeclaration>
        where TDeclaration : TConfig.Declarative.IDeclaration
    {
        string Name { get; }

        ValidationResult Validate(TDeclaration declaration);

        IReadOnlyList<ValidationResult> ValidateAll(IEnumerable<TDeclaration> declarations);
    }
}
