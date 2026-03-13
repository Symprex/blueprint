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
    /// Builds record declarations and record property metadata.
    /// </summary>
    public sealed class RecordBuilder : BuilderBase<RecordBuilder>
    {
        private readonly List<RecordPropertyBuilder> _properties = new List<RecordPropertyBuilder>();
        private readonly List<string> _attributes = new List<string>();
        private DocumentationComment? _xmlDoc;

        internal RecordBuilder(string name, CodeBuilder codeBuilder)
        {
            Name = name;
            Builder = codeBuilder;
        }

        public string Name { get; }

        public string FullyQualifiedName => $"{Builder.Namespace}.{Name}";

        public CodeBuilder Builder { get; internal set; }

        public Accessibility? AccessModifier { get; private set; }

        public RecordPropertyType PropertyType { get; private set; } = RecordPropertyType.Positional;

        /// <summary>
        /// Sets an XML summary comment for the generated record.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public RecordBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };

            return this;
        }

        /// <summary>
        /// Configures <inheritdoc/> XML documentation for the generated record.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public RecordBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures <inheritdoc/> XML documentation for the generated record.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public RecordBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Configures the record to emit positional properties.
        /// </summary>
        /// <returns>The generated result.</returns>
        public RecordBuilder UsePositionalProperties()
        {
            PropertyType = RecordPropertyType.Positional;
            return this;
        }

        /// <summary>
        /// Configures the record to emit init-only properties.
        /// </summary>
        /// <returns>The generated result.</returns>
        public RecordBuilder UseInitProperties()
        {
            PropertyType = RecordPropertyType.Init;
            return this;
        }

        /// <summary>
        /// Adds a property definition to the record being built.
        /// </summary>
        /// <param name="type">The property type name.</param>
        /// <param name="name">The property name.</param>
        /// <param name="accessModifier">The access modifier applied to the property.</param>
        /// <returns>The created <see cref="RecordPropertyBuilder"/> for additional configuration.</returns>
        public RecordPropertyBuilder AddProperty(string type,
            string name,
            Accessibility accessModifier = Accessibility.Public)
        {
            var prop = new RecordPropertyBuilder(type, name, accessModifier, this);
            _properties.Add(prop);
            return prop;
        }

        /// <summary>
        /// Marks public record.
        /// </summary>
        /// <returns>The generated result.</returns>
        public RecordBuilder MakePublicRecord() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Marks internal record.
        /// </summary>
        /// <returns>The generated result.</returns>
        public RecordBuilder MakeInternalRecord() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Configures access modifier.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public RecordBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Builds build.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string Build() => Builder.Build();

        internal override void Write(in CodeWriter writer)
        {
            _xmlDoc?.Write(writer);

            foreach (var attribute in _attributes)
                writer.AppendLine($"[{attribute}]");

            if (PropertyType == RecordPropertyType.Init)
            {
                using (writer.Block($"{AccessModifier.Code()} record {Name}"))
                {
                    foreach (RecordPropertyBuilder property in _properties)
                    {
                        writer.AppendLine(property.ToInitProperty());
                    }
                }

                return;
            }

            var properties = _properties.Any() ? string.Join(", ", _properties.Select(x => x.ToPositionalProperty())) : string.Empty;
            writer.AppendLine($"{AccessModifier.Code()} record {Name}({properties});");
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public override RecordBuilder AddNamespaceImport(string importedNamespace)
        {
            Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override RecordBuilder AddNamespaceImport(ISymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override RecordBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public override RecordBuilder AddAssemblyAttribute(string attribute)
        {
            Builder.AddAssemblyAttribute(attribute);
            return this;
        }
    }
}
