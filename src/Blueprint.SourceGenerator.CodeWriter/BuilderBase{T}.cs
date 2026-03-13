using System;
using Microsoft.CodeAnalysis;

#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Provides the rendering infrastructure shared by builders that need pragma warning suppression around emitted source.
    /// </summary>
    public abstract class BuilderBase<T> : BuilderBase
        where T : BuilderBase
    {
        /// <summary>
        /// Adds namespace import to the current generated declaration.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public abstract T AddNamespaceImport(string importedNamespace);

        /// <summary>
        /// Adds namespace import to the current generated declaration.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public abstract T AddNamespaceImport(ISymbol symbol);

        /// <summary>
        /// Adds namespace import to the current generated declaration.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public abstract T AddNamespaceImport(INamespaceSymbol symbol);

        /// <summary>
        /// Adds assembly attribute to the current generated declaration.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public abstract T AddAssemblyAttribute(string attribute);

        /// <summary>
        /// Wraps the generated declaration in pragma directives that suppress the specified compiler warning.
        /// </summary>
        /// <param name="buildCode">The compiler warning ID to disable while this declaration is emitted.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public T DisableWarning(string buildCode)
        {
            _pragmaWarnings.Add(buildCode);
            if (this is T thisAsT)
                return thisAsT;

            throw new InvalidOperationException($"The Builder must be of type {typeof(T).FullName}");
        }
    }
}
