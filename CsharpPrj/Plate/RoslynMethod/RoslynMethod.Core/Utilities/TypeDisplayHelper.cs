using Microsoft.CodeAnalysis;
using System;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Utilities
{
    public static class TypeDisplayHelper
    {
        public static readonly SymbolDisplayFormat FullyQualifiedNoAliasFormat =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                    & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly SymbolDisplayFormat MinimalFormat =
            SymbolDisplayFormat.MinimallyQualifiedFormat;

        public static readonly SymbolDisplayFormat MethodSignatureFormat =
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                memberOptions: SymbolDisplayMemberOptions.IncludeParameters |
                               SymbolDisplayMemberOptions.IncludeType,
                parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                                  SymbolDisplayParameterOptions.IncludeParamsRefOut,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static string GetFullyQualifiedName(ITypeSymbol type)
        {
            if (type == null) return string.Empty;

            var fullName = type.ToDisplayString(FullyQualifiedNoAliasFormat);
            if (fullName.StartsWith("global::"))
                fullName = fullName.Substring(8);

            return fullName;
        }

        public static string GetMinimalName(ITypeSymbol type)
        {
            return type?.ToDisplayString(MinimalFormat) ?? string.Empty;
        }

        public static string GetMethodSignature(IMethodSymbol method)
        {
            return method?.ToDisplayString(MethodSignatureFormat) ?? string.Empty;
        }

        public static string CleanGlobalPrefix(string fullName)
        {
            if (fullName != null && fullName.StartsWith("global::"))
                return fullName.Substring(8);
            return fullName ?? string.Empty;
        }
    }
}
