using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated enum declaration and the values that it contains.
    /// </summary>
    public class EnumBuilder : IBuilder
    {
        private readonly List<string> _attributes = new List<string>();
        private readonly List<EnumValueBuilder> _values = new List<EnumValueBuilder>();
        private DocumentationComment? _xmlDoc;

        /// <summary>
        /// Initializes a new instance attached to the supplied file builder.
        /// </summary>
        /// <param name="name">The enum name to emit.</param>
        /// <param name="builder">The file builder that owns this declaration.</param>
        internal EnumBuilder(string name, CodeBuilder builder)
        {
            this.Name = name;
            this.Builder = builder;
        }

        /// <summary>
        /// Gets the CodeBuilder that owns this generated enum.
        /// </summary>
        public CodeBuilder Builder { get; internal set; }

        /// <summary>
        /// Gets the enum name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the fully qualified enum name composed from the owning namespace and the enum name.
        /// </summary>
        public string FullyQualifiedName => $"{this.Builder.Namespace}.{this.Name}";

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated enum.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated enum.
        /// </summary>
        /// <param name="summary">The text to place inside the emitted summary documentation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder WithSummary(string summary)
        {
            this._xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures the generated enum to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated enum. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds an enum value to the generated declaration and returns its builder.
        /// </summary>
        /// <param name="name">The enum member name to emit.</param>
        /// <param name="numericValue">The numeric value to assign to the generated enum member.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public EnumValueBuilder AddValue(string name, int? numericValue = null)
        {
            var builder = new EnumValueBuilder(name, this, numericValue);
            this._values.Add(builder);
            return builder;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this enum.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this enum.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder AddNamespaceImport(ISymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this enum.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds an attribute to the generated enum if it is not already present.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!this._attributes.Contains(sanitized))
            {
                this._attributes.Add(sanitized);
            }

            return this;
        }

        /// <summary>
        /// Sets the generated enum to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder MakePublicEnum()
        {
            return this.WithAccessModifier(Accessibility.Public);
        }

        /// <summary>
        /// Sets the generated enum to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder MakeInternalEnum()
        {
            return this.WithAccessModifier(Accessibility.Internal);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated enum.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumBuilder WithAccessModifier(Accessibility accessModifier)
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

        /// <inheritdoc/>
        void IBuilder.Write(in CodeWriter writer)
        {
            this._xmlDoc?.Write(writer);

            var queue = new Queue<IBuilder>();
            this._values.OrderBy(x => x.Value)
                //.ThenBy(x => x.Name)
                .ToList()
                .ForEach(x => queue.Enqueue(x));

            foreach (var attr in this._attributes.OrderBy(x => x))
            {
                writer.AppendLine($"[{attr}]");
            }

            var parts = new[]
            {
                this.AccessModifier.Code(),
                "enum",
                this.Name
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
