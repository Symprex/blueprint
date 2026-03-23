using Microsoft.CodeAnalysis;

#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter.Internals
{
    /// <summary>
    /// Converts Roslyn <see cref="Microsoft.CodeAnalysis.Accessibility"/> values to the C# keyword text used in generated source.
    /// </summary>
    internal static class AccessibilityExtensions
    {
        /// <summary>
        /// Converts a Roslyn accessibility value into the C# keyword text that should be emitted.
        /// </summary>
        /// <param name="accessModifier">The Roslyn accessibility value to format.</param>
        /// <returns>The C# accessibility keyword text, or <see langword="null"/> when no modifier should be emitted.</returns>
        public static string? Code(this Accessibility accessModifier)
        {
            return accessModifier switch
            {
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.NotApplicable => null,
                _ => accessModifier.ToString().ToLowerInvariant()
            };
        }

        /// <summary>
        /// Converts a nullable Roslyn accessibility value into the C# keyword text that should be emitted.
        /// </summary>
        /// <param name="accessModifier">The accessibility value to format.</param>
        /// <returns>The C# accessibility keyword text, or <see langword="null"/> when no modifier should be emitted.</returns>
        public static string? Code(this Accessibility? accessModifier)
        {
            return accessModifier.HasValue ? accessModifier.Value.Code() : null;
        }

        /// <summary>
        /// Converts a nullable Roslyn accessibility value into emitted C# keyword text, falling back to a default when needed.
        /// </summary>
        /// <param name="accessModifier">The optional accessibility value to format.</param>
        /// <param name="defaultValue">The fallback accessibility to use when <paramref name="accessModifier"/> is <see langword="null"/>.</param>
        /// <returns>The C# accessibility keyword text that should be emitted.</returns>
        public static string? Code(this Accessibility? accessModifier, Accessibility defaultValue)
        {
            return accessModifier.HasValue ? accessModifier.Value.Code() : defaultValue.Code();
        }
    }
}
