
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Resolvers
{
    public class DeclarationResolver<TDeclaration>
        : IDeclarationResolver<IDictionary<string, object?>, TDeclaration>
        where TDeclaration : IDeclaration
    {
        private readonly string _name;
        private readonly Func<IDictionary<string, object?>, TDeclaration> _factory;

        public DeclarationResolver(
            string name,
            Func<IDictionary<string, object?>, TDeclaration> factory)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public string Name => _name;

        public ResolutionResult<TDeclaration> Resolve(IDictionary<string, object?> source)
        {
            try
            {
                var declaration = _factory(source);
                return ResolutionResult<TDeclaration>.Success(declaration);
            }
            catch (Exception ex)
            {
                return ResolutionResult<TDeclaration>.Failure(
                    $"解析声明失败: {ex.Message}", ex);
            }
        }

        public ResolutionResult<IReadOnlyList<TDeclaration>> ResolveAll(
            IEnumerable<IDictionary<string, object?>> sources)
        {
            try
            {
                var results = new List<TDeclaration>();
                foreach (var source in sources)
                {
                    var result = Resolve(source);
                    if (result.IsSuccess && result.Value != null)
                        results.Add(result.Value);
                }
                return ResolutionResult<IReadOnlyList<TDeclaration>>.Success(results.AsReadOnly());
            }
            catch (Exception ex)
            {
                return ResolutionResult<IReadOnlyList<TDeclaration>>.Failure(
                    $"批量解析声明失败: {ex.Message}", ex);
            }
        }
    }
}
