using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter
{
    internal class GenericCollection : List<GenericBuilder>
    {
        /// <summary>
        /// Converts string.
        /// </summary>
        /// <returns>The generated result.</returns>
        public override string ToString()
        {
            if (!this.Any())
                return string.Empty;

            return $"<{string.Join(", ", this.Select(x => x.Name))}>";
        }

        /// <summary>
        /// Performs the contraints operation.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string[] Contraints() =>
            this.Select(x => x.ToString())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
    }
}
