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
            this.Name = name;
            this.Builder = codeBuilder;
        }

        /// <summary>
        /// Gets the record name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the fully qualified record name composed from the owning namespace and the record name.
        /// </summary>
        public string FullyQualifiedName => $"{this.Builder.Namespace}.{this.Name}";

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
            this._xmlDoc = new SummaryDocumentationComment { Summary = summary };

            return this;
        }

        /// <summary>
        /// Configures the generated record to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated record. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Configures the generated record to emit positional properties.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder UsePositionalProperties()
        {
            this.PropertyType = RecordPropertyType.Positional;
            return this;
        }

        /// <summary>
        /// Configures the generated record to emit init-only properties.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder UseInitProperties()
        {
            this.PropertyType = RecordPropertyType.Init;
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
            this._properties.Add(prop);
            return prop;
        }

        /// <summary>
        /// Sets the generated record to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder MakePublicRecord()
        {
            return this.WithAccessModifier(Accessibility.Public);
        }

        /// <summary>
        /// Sets the generated record to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder MakeInternalRecord()
        {
            return this.WithAccessModifier(Accessibility.Internal);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated record.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordBuilder WithAccessModifier(Accessibility accessModifier)
        {
            this.AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Renders the owning CodeBuilder and returns the generated C# source text.
        /// </summary>
        /// <returns>The rendered C# source text.</returns>
        public string Build()
        {
            return this.Builder.Build();
        }

        internal override void Write(in CodeWriter writer)
        {
            this._xmlDoc?.Write(writer);

            foreach (var attribute in this._attributes)
            {
                writer.AppendLine($"[{attribute}]");
            }

            if (this.PropertyType == RecordPropertyType.Init)
            {
                using (writer.Block($"{this.AccessModifier.Code()} record {this.Name}"))
                {
                    foreach (var property in this._properties)
                    {
                        writer.AppendLine(property.ToInitProperty());
                    }
                }

                return;
            }

            var properties = this._properties.Any() ? string.Join(", ", this._properties.Select(x => x.ToPositionalProperty())) : string.Empty;
            writer.AppendLine($"{this.AccessModifier.Code()} record {this.Name}({properties});");
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(ISymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this record.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this record.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override RecordBuilder AddAssemblyAttribute(string attribute)
        {
            this.Builder.AddAssemblyAttribute(attribute);
            return this;
        }
    }
}
