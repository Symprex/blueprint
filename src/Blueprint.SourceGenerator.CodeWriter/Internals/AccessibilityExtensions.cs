using Microsoft.CodeAnalysis;

#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter.Internals
{
    internal static class AccessibilityExtensions
    {
        /// <summary>
        /// Performs the code operation.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public static string? Code(this Accessibility accessModifier) =>
            accessModifier switch
            {
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.NotApplicable => null,
                _ => accessModifier.ToString().ToLowerInvariant()
            };

        /// <summary>
        /// Performs the code operation.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public static string? Code(this Accessibility? accessModifier) =>
            accessModifier.HasValue ? accessModifier.Value.Code() : null;

        /// <summary>
        /// Performs the code operation.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <param name="defaultValue">The default value value.</param>
        /// <returns>The generated result.</returns>
        public static string? Code(this Accessibility? accessModifier, Accessibility defaultValue) =>
            accessModifier.HasValue ? accessModifier.Value.Code() : defaultValue.Code();
    }
}
