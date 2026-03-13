using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter
{
    internal class GenericCollection : List<GenericBuilder>
    {
        /// <summary>
        /// Formats the generic parameter list used in a declaration header.
        /// </summary>
        /// <returns>The generic parameter list, or an empty string when no generic parameters have been configured.</returns>
        public override string ToString()
        {
            return !this.Any() ? string.Empty : $"<{string.Join(", ", this.Select(x => x.Name))}>";
        }

        /// <summary>
        /// Formats the configured generic constraints as individual <c>where</c> clauses.
        /// </summary>
        /// <returns>The non-empty constraint clauses that should be emitted after the declaration header.</returns>
        public string[] Contraints()
        {
            return this.Select(x => x.ToString())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
        }
    }
}
