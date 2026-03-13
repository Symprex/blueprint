using System.Collections.Generic;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents enum builder.
    /// </summary>
    public class EnumBuilder : IBuilder
    {
        private readonly List<string> _attributes = new List<string>();
        private readonly List<EnumValueBuilder> _values = new List<EnumValueBuilder>();
        private DocumentationComment? _xmlDoc;

        internal EnumBuilder(string name, CodeBuilder builder)
        {
            Name = name;
            Builder = builder;
        }

        public CodeBuilder Builder { get; internal set; }

        public string Name { get; }

        public string FullyQualifiedName => $"{Builder.Namespace}.{Name}";

        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Configures summary.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds value.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="numericValue">The numeric value value.</param>
        /// <returns>The generated result.</returns>
        public EnumValueBuilder AddValue(string name, int? numericValue = null)
        {
            var builder = new EnumValueBuilder(name, this, numericValue);
            _values.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder AddNamespaceImport(string importedNamespace)
        {
            Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder AddNamespaceImport(ISymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Marks public enum.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EnumBuilder MakePublicEnum() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Marks internal enum.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EnumBuilder MakeInternalEnum() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Configures access modifier.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public EnumBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Builds build.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string Build() => Builder.Build();

        void IBuilder.Write(in CodeWriter writer)
        {
            _xmlDoc?.Write(writer);

            var queue = new Queue<IBuilder>();
            _values.OrderBy(x => x.Value)
                //.ThenBy(x => x.Name)
                .ToList()
                .ForEach(x => queue.Enqueue(x));

            foreach(var attr in _attributes.OrderBy(x => x))
            {
                writer.AppendLine($"[{attr}]");
            }

            var parts = new[]
            {
                AccessModifier.Code(),
                "enum",
                Name
            };

            using (writer.Block(string.Join(" ", parts.Where(x => !string.IsNullOrEmpty(x)))))
            {
                while (queue.Any())
                {
                    var value = queue.Dequeue();
                    value.Write(writer);

                    if (queue.Any())
                    {
                        writer.AppendUnindentedLine(",");
                        writer.NewLine();
                    }
                    else
                    {
                        writer.NewLine();
                    }
                }
            }
        }
    }
}
