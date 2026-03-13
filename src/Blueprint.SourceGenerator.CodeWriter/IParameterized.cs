using System.Collections.Generic;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Defines the contract for iparameterized.
    /// </summary>
    public interface IParameterized<T>
        where T : BuilderBase<T>
    {
        T Parent { get; }

        List<ParameterBuilder<T>> Parameters { get; }
    }
}
