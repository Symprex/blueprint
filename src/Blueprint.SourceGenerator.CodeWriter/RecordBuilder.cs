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
    /// Builds a generated record declaration and the properties that it emits.
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

        /// <summary>
        /// Gets the record name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the fully qualified record name composed from the owning namespace and the record name.
        /// </summary>
        public string FullyQualifiedName => $"{Builder.Namespace}.{Name}";

        /// <summary>
        /// Gets the CodeBuilder that owns this generated record.
        /// </summary>
        public CodeBuilder Builder { get; internal set; }

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated record.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Gets how properties will be emitted for the generated record.
        /// </summary>
        public RecordPropertyType PropertyType { get; private set; } = RecordPropertyType.Positional;

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated record.
        /// </summary>
        /// <param name="summary">The text to place inside the emitted summary documentation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };

            return this;
        }

        /// <summary>
        /// Configures the generated record to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated record. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Configures the generated record to emit positional properties.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder UsePositionalProperties()
        {
            PropertyType = RecordPropertyType.Positional;
            return this;
        }

        /// <summary>
        /// Configures the generated record to emit init-only properties.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder UseInitProperties()
        {
            PropertyType = RecordPropertyType.Init;
            return this;
        }

        /// <summary>
        /// Adds a property definition to the generated record and returns its builder.
        /// </summary>
        /// <param name="type">The C# type text to emit for the generated record property.</param>
        /// <param name="name">The property name to emit for the generated record property.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public RecordPropertyBuilder AddProperty(string type,
            string name,
            Accessibility accessModifier = Accessibility.Public)
        {
            var prop = new RecordPropertyBuilder(type, name, accessModifier, this);
            _properties.Add(prop);
            return prop;
        }

        /// <summary>
        /// Sets the generated record to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder MakePublicRecord() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Sets the generated record to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder MakeInternalRecord() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Sets the accessibility emitted for the generated record.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Renders the owning CodeBuilder and returns the generated C# source text.
        /// </summary>
        /// <returns>The rendered C# source text.</returns>
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
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(string importedNamespace)
        {
            Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(ISymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this record.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddAssemblyAttribute(string attribute)
        {
            Builder.AddAssemblyAttribute(attribute);
            return this;
        }
    }
}
