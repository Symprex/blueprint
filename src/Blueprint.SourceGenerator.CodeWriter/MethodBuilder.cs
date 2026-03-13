using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated method.
    /// Configure the signature first, then use <c>WithBody</c> and the <see cref="ICodeWriterExtensions"/> helpers to describe the emitted statements without hand-writing raw source.
    /// </summary>
    public class MethodBuilder : BuilderBase<MethodBuilder>, IBuilder, IParameterized<MethodBuilder>
    {
        private readonly List<string> _attributes = new List<string>();
        private readonly GenericCollection _generics = new GenericCollection();
        private DocumentationComment? _xmlDoc;
        private readonly List<ParameterBuilder<MethodBuilder>> _parameters = new List<ParameterBuilder<MethodBuilder>>();
        private bool _override;
        private bool _virtual;

        private Action<ICodeWriter>? _methodBodyWriter;

        internal MethodBuilder(string name, Accessibility? accessModifier, ClassBuilder builder)
        {
            this.Name = name;
            this.AccessModifier = accessModifier;
            this.Class = builder;
        }

        List<ParameterBuilder<MethodBuilder>> IParameterized<MethodBuilder>.Parameters => this._parameters;
        MethodBuilder IParameterized<MethodBuilder>.Parent => this;

        /// <summary>
        /// Gets the parameters currently configured for the generated method signature.
        /// </summary>
        public IReadOnlyCollection<ParameterBuilder<MethodBuilder>> Parameters => this._parameters;

        /// <summary>
        /// Gets the method name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the return type that will be emitted for the generated method.
        /// </summary>
        public string? ReturnType { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated method will be emitted with the async modifier.
        /// </summary>
        public bool IsAsync { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated method will be emitted with the abstract modifier.
        /// </summary>
        public bool IsAbstract { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the configured body callback actually emits any statements.
        /// </summary>
        public bool HasBody
        {
            get
            {
                if (this._methodBodyWriter is null)
                {
                    return false;
                }

                var writer = new CodeWriter(IndentStyle.Spaces);
                this._methodBodyWriter(writer);
                return !string.IsNullOrEmpty(writer.ToString());
            }
        }

        /// <summary>
        /// Gets the ClassBuilder that owns this generated method.
        /// </summary>
        public ClassBuilder Class { get; }

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated method.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated method will be emitted with the static modifier.
        /// </summary>
        public bool IsStatic { get; private set; }

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated method.
        /// </summary>
        /// <param name="summary">The caller-facing description of what the generated method does or when it should be used.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithSummary(string summary)
        {
            if (this._xmlDoc is null || this._xmlDoc is not SummaryDocumentationComment summaryDoc)
            {
                this._xmlDoc = new ParameterDocumentationComment { Summary = summary };
            }
            else
            {
                summaryDoc.Summary = summary;
            }

            return this;
        }

        /// <summary>
        /// Configures the generated method to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated method. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds or replaces XML documentation for a parameter on the generated method.
        /// </summary>
        /// <param name="paramName">The name of the generated parameter whose XML documentation should be updated.</param>
        /// <param name="documentation">The guidance to emit for callers consuming that generated parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithParameterDoc(string paramName, string documentation)
        {
            if (this._xmlDoc is null)
            {
                this._xmlDoc = new ParameterDocumentationComment();
            }

            if (this._xmlDoc is not ParameterDocumentationComment parameterDoc)
            {
                throw new Exception("DocumentationComment has already been initialized with a non ParameterDocumentationComment");
            }

            parameterDoc.AddParameter(paramName, documentation);

            return this;
        }

        /// <summary>
        /// Adds a generic type parameter to the generated method and optionally configures its constraints.
        /// </summary>
        /// <param name="name">The generic parameter name to add to the method signature.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder AddGeneric(string name)
        {
            return this.AddGeneric(name, _ => { });
        }

        /// <summary>
        /// Adds a generic type parameter to the generated method and optionally configures its constraints.
        /// </summary>
        /// <param name="name">The generic parameter name to add to the method signature.</param>
        /// <param name="configureBuilder">A callback that can add the emitted <c>where</c> constraints before the generic is attached.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
        {
            if (configureBuilder is null)
            {
                throw new ArgumentNullException(nameof(configureBuilder));
            }
            else if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            else if (this._generics.Any(x => x.Name == name))
            {
                throw new ArgumentException($"The argument {name} already exists");
            }

            var builder = new GenericBuilder(name);
            configureBuilder(builder);
            this._generics.Add(builder);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this method.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override MethodBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this method.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override MethodBuilder AddNamespaceImport(ISymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this method.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override MethodBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this method.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override MethodBuilder AddAssemblyAttribute(string attribute)
        {
            this.Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds a sibling property to the owning class while continuing fluent method configuration.
        /// </summary>
        /// <param name="name">The generated name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
        {
            return this.Class.AddProperty(name, accessModifier);
        }

        /// <summary>
        /// Marks the generated method as async.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakeAsync()
        {
            this.IsAsync = true;
            return this;
        }

        /// <summary>
        /// Sets whether the generated method should be emitted with the abstract modifier.
        /// </summary>
        /// <param name="isAbstract">True to emit the generated declaration as abstract; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder Abstract(bool isAbstract = true)
        {
            this.IsAbstract = isAbstract;
            return this;
        }

        /// <summary>
        /// Sets the generated method to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakePublicMethod()
        {
            return this.WithAccessModifier(Accessibility.Public);
        }

        /// <summary>
        /// Sets the generated method to private accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakePrivateMethod()
        {
            return this.WithAccessModifier(Accessibility.Private);
        }

        /// <summary>
        /// Sets the generated method to protected accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakeProtectedMethod()
        {
            return this.WithAccessModifier(Accessibility.Protected);
        }

        /// <summary>
        /// Sets the generated method to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakeInternalMethod()
        {
            return this.WithAccessModifier(Accessibility.Internal);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated method.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithAccessModifier(Accessibility accessModifier)
        {
            this.AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Sets whether the generated method should be emitted with the override modifier.
        /// </summary>
        /// <param name="override">True to emit the generated declaration with the override modifier; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder Override(bool @override = true)
        {
            this._override = @override;
            return this;
        }

        /// <summary>
        /// Marks the generated method as static.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakeStaticMethod()
        {
            this.IsStatic = true;
            return this;
        }

        /// <summary>
        /// Marks the generated method as virtual.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder MakeVirtualMethod()
        {
            this._virtual = true;
            return this;
        }

        /// <summary>
        /// Adds attribute to the current generated method.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!this._attributes.Contains(sanitized))
            {
                this._attributes.Add(sanitized);
            }

            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated method body.
        /// </summary>
        /// <param name="writerDelegate">A callback that writes the generated method body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithBody(Action<ICodeWriter> writerDelegate)
        {
            this._methodBodyWriter = writerDelegate;
            return this;
        }

        /// <summary>
        /// Sets the return type emitted for the generated method.
        /// </summary>
        /// <param name="returnType">The return type used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public MethodBuilder WithReturnType(string returnType)
        {
            this.ReturnType = returnType;
            return this;
        }

        internal override void Write(in CodeWriter writer)
        {
            var output = this.ReturnType is null || string.IsNullOrEmpty(this.ReturnType) ? "void" : this.ReturnType.Trim();
            if (this.IsAsync)
            {
                output = $"async {output}";
            }

            if (this._override)
            {
                output = $"override {output}";
            }
            else if (this._virtual)
            {
                output = $"virtual {output}";
            }
            else if (this.IsAbstract)
            {
                output = $"abstract {output}";
            }
            else if (this.IsStatic)
            {
                output = $"static {output}";
            }

            var parameters = string.Join(", ", this._parameters.Select(x => x.ToString()));
            output = $"{this.AccessModifier.Code()} {output} {this.Name}{this._generics}({parameters})";

            if (this._xmlDoc is ParameterDocumentationComment parameterDocumentation)
            {
                parameterDocumentation.RemoveUnusedParameters(this._parameters);
            }

            this._xmlDoc?.Write(writer);

            foreach (var attribute in this._attributes)
            {
                writer.AppendLine($"[{attribute}]");
            }

            if (this.IsAbstract)
            {
                writer.AppendLine($"{output.Trim()};");
                return;
            }

            using (writer.Block(output.Trim(), this._generics.Contraints()))
            {
                this._methodBodyWriter?.Invoke(writer);
            }
        }
    }
}
