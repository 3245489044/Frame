using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class AnalysisSkeleton
    {
        private readonly SemanticModel _semanticModel;
        private readonly CancellationToken _cancellationToken;

        public AnalysisSkeleton(SemanticModel semanticModel, CancellationToken cancellationToken = default)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _cancellationToken = cancellationToken;
        }

        public List<EnhancedCodeElement> AnalyzeElements(SyntaxNode root)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var elements = new List<EnhancedCodeElement>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var element = AnalyzeMethod(method);
                if (element != null)
                    elements.Add(element);
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var element = AnalyzeProperty(property);
                if (element != null)
                    elements.Add(element);
            }

            foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var element = AnalyzeConstructor(constructor);
                if (element != null)
                    elements.Add(element);
            }

            return elements;
        }

        public async Task<List<EnhancedCodeElement>> AnalyzeElementsAsync(
            SyntaxNode root,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elements = new ConcurrentBag<EnhancedCodeElement>();
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken).Token;

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();
            var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToList();

            int totalItems = methods.Count + properties.Count + constructors.Count;
            int processedItems = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(methods, method =>
                {
                    linkedToken.ThrowIfCancellationRequested();
                    var element = AnalyzeMethod(method);
                    if (element != null)
                        elements.Add(element);

                    Interlocked.Increment(ref processedItems);
                    progress?.Report(new AnalysisProgress(processedItems, totalItems, "分析方法"));
                });

                Parallel.ForEach(properties, property =>
                {
                    linkedToken.ThrowIfCancellationRequested();
                    var element = AnalyzeProperty(property);
                    if (element != null)
                        elements.Add(element);

                    Interlocked.Increment(ref processedItems);
                    progress?.Report(new AnalysisProgress(processedItems, totalItems, "分析属性"));
                });

                Parallel.ForEach(constructors, constructor =>
                {
                    linkedToken.ThrowIfCancellationRequested();
                    var element = AnalyzeConstructor(constructor);
                    if (element != null)
                        elements.Add(element);

                    Interlocked.Increment(ref processedItems);
                    progress?.Report(new AnalysisProgress(processedItems, totalItems, "分析构造函数"));
                });
            }, linkedToken);

            return elements.ToList();
        }

        private EnhancedCodeElement AnalyzeMethod(MethodDeclarationSyntax method)
        {
            var containingType = SyntaxHelper.GetContainingTypeName(method);
            var element = new EnhancedCodeElement(
                $"{ElementType.Method}_{method.Identifier.Text}_{SyntaxHelper.GetLineNumber(method)}_{containingType}",
                method.Identifier.Text,
                ElementType.Method,
                SyntaxHelper.GetLineNumber(method),
                containingType)
            {
                ReturnType = method.ReturnType.ToString(),
                Accessibility = AccessibilityConverter.GetAccessibilityString(method.Modifiers),
                IsStatic = SyntaxHelper.IsStatic(method.Modifiers),
                IsVirtual = SyntaxHelper.IsVirtual(method.Modifiers),
                IsAbstract = SyntaxHelper.IsAbstract(method.Modifiers),
                ComplexityScore = ComplexityCalculator.CalculateMethodComplexity(method),
                Parameters = method.ParameterList.Parameters.Select(p => p.ToString()).ToList()
            };

            if (_semanticModel != null)
            {
                element.IsPure = PurityAnalyzer.AnalyzeMethodPurity(method, _semanticModel);
                element.HasSideEffects = PurityAnalyzer.AnalyzeSideEffects(method);
            }

            return element;
        }

        private EnhancedCodeElement AnalyzeProperty(PropertyDeclarationSyntax property)
        {
            var containingType = SyntaxHelper.GetContainingTypeName(property);
            return new EnhancedCodeElement(
                $"{ElementType.Property}_{property.Identifier.Text}_{SyntaxHelper.GetLineNumber(property)}_{containingType}",
                property.Identifier.Text,
                ElementType.Property,
                SyntaxHelper.GetLineNumber(property),
                containingType)
            {
                ReturnType = property.Type.ToString(),
                Accessibility = AccessibilityConverter.GetAccessibilityString(property.Modifiers),
                IsStatic = SyntaxHelper.IsStatic(property.Modifiers)
            };
        }

        private EnhancedCodeElement AnalyzeConstructor(ConstructorDeclarationSyntax constructor)
        {
            var containingType = SyntaxHelper.GetContainingTypeName(constructor);
            return new EnhancedCodeElement(
                $"{ElementType.Constructor}_{constructor.Identifier.Text}_{SyntaxHelper.GetLineNumber(constructor)}_{containingType}",
                constructor.Identifier.Text,
                ElementType.Constructor,
                SyntaxHelper.GetLineNumber(constructor),
                containingType)
            {
                Accessibility = AccessibilityConverter.GetAccessibilityString(constructor.Modifiers),
                ComplexityScore = ComplexityCalculator.CalculateConstructorComplexity(constructor),
                Parameters = constructor.ParameterList.Parameters.Select(p => p.ToString()).ToList()
            };
        }
    }

    public class AnalysisProgress
    {
        public int ProcessedItems { get; }
        public int TotalItems { get; }
        public string CurrentPhase { get; }
        public double Percentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100 : 0;

        public AnalysisProgress(int processedItems, int totalItems, string currentPhase)
        {
            ProcessedItems = processedItems;
            TotalItems = totalItems;
            CurrentPhase = currentPhase;
        }
    }
}
