using System;
using Microsoft.CodeAnalysis;

#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents builder base.
    /// </summary>
    public abstract class BuilderBase<T> : BuilderBase
        where T : BuilderBase
    {
        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public abstract T AddNamespaceImport(string importedNamespace);

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public abstract T AddNamespaceImport(ISymbol symbol);

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public abstract T AddNamespaceImport(INamespaceSymbol symbol);

        /// <summary>
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public abstract T AddAssemblyAttribute(string attribute);

        /// <summary>
        /// Disables warning.
        /// </summary>
        /// <param name="buildCode">The build code value.</param>
        /// <returns>The generated result.</returns>
        public T DisableWarning(string buildCode)
        {
            _pragmaWarnings.Add(buildCode);
            if (this is T thisAsT)
                return thisAsT;

            throw new InvalidOperationException($"The Builder must be of type {typeof(T).FullName}");
        }
    }
}
