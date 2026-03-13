using System.Collections.Generic;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Exposes the parameter collection for builders that can emit method, constructor, or delegate signatures.
    /// </summary>
    public interface IParameterized<T>
        where T : BuilderBase<T>
    {
        /// <summary>
        /// Gets the owning builder returned after parameter configuration is complete.
        /// </summary>
        T Parent { get; }

        /// <summary>
        /// Gets the mutable parameter builders that make up the generated signature.
        /// </summary>
        List<ParameterBuilder<T>> Parameters { get; }
    }
}
