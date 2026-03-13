using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated constructor.
    /// Use it to declare parameters, chain to <c>this</c> or <c>base</c> constructors, and describe the emitted body without assembling constructor source by hand.
    /// </summary>
    public sealed class ConstructorBuilder : BuilderBase<ConstructorBuilder>, IParameterized<ConstructorBuilder>
    {
        private readonly List<ParameterBuilder<ConstructorBuilder>> _parameters = new List<ParameterBuilder<ConstructorBuilder>>();
        private readonly List<string> _attributes = new List<string>();
        private DocumentationComment? _xmlDoc;

        private Action<ICodeWriter>? _methodBodyWriter;

        private Func<string> _baseCall;

        internal ConstructorBuilder(Accessibility? accessModifier, ClassBuilder classBuilder)
        {
            this.AccessModifier = accessModifier;
            this.Class = classBuilder;
            this._baseCall = () => string.Empty;
        }

        List<ParameterBuilder<ConstructorBuilder>> IParameterized<ConstructorBuilder>.Parameters => this._parameters;

        ConstructorBuilder IParameterized<ConstructorBuilder>.Parent => this;

        /// <summary>
        /// Gets the parameters currently configured for the generated constructor signature.
        /// </summary>
        public IReadOnlyCollection<ParameterBuilder<ConstructorBuilder>> Parameters => this._parameters;

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated constructor.
        /// </summary>
        public Accessibility? AccessModifier { get; }

        /// <summary>
        /// Gets the ClassBuilder that owns this generated constructor.
        /// </summary>
        public ClassBuilder Class { get; }

        internal int Count => this._parameters.Count;

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated constructor.
        /// </summary>
        /// <param name="summary">The caller-facing description of why the generated constructor exists or what it initializes.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithSummary(string summary)
        {
            if (this._xmlDoc is not null && this._xmlDoc is SummaryDocumentationComment summaryComment)
            {
                summaryComment.Summary = summary;
            }
            else
            {
                this._xmlDoc = new ParameterDocumentationComment { Summary = summary };
            }

            return this;
        }

        /// <summary>
        /// Configures the generated constructor to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated constructor. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds or replaces XML documentation for a parameter on the generated constructor.
        /// </summary>
        /// <param name="paramName">The name of the generated parameter whose XML documentation should be updated.</param>
        /// <param name="documentation">The guidance to emit for callers consuming that generated parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithParameterDoc(string paramName, string documentation)
        {
            if (this._xmlDoc is not null && this._xmlDoc is not ParameterDocumentationComment)
            {
                throw new Exception("Documentation Comment has already been initialized using an InheritDoc.");
            }

            var parameterDoc = this._xmlDoc as ParameterDocumentationComment;
            parameterDoc?.AddParameter(paramName, documentation);

            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this constructor.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddAssemblyAttribute(string attribute)
        {
            this.Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds an attribute to the generated constructor if it is not already present.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!this._attributes.Contains(sanitized))
            {
                this._attributes.Add(sanitized);
            }

            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated constructor body.
        /// </summary>
        /// <param name="writerDelegate">A callback that writes the generated constructor body, typically by using <see cref="ICodeWriterExtensions"/> helpers to express statements and control flow.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBody(Action<ICodeWriter> writerDelegate)
        {
            this._methodBodyWriter = writerDelegate;
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type.
        /// </summary>
        /// <remarks>Use this overload when the target constructor takes no arguments.</remarks>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall()
        {
            this._baseCall = () => ": this()";
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type while also declaring matching parameters on this constructor.
        /// </summary>
        /// <param name="parameters">The parameter-type-to-argument mapping to declare on this constructor and forward into the emitted <c>this(...)</c> call.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall(Dictionary<string, string> parameters)
        {
            foreach (var parameter in parameters)
            {
                this.AddParameter(parameter.Key, parameter.Value);
            }

            this._baseCall = () =>
            {
                var output = parameters.Select(x => x.Value);
                return $": this({string.Join(", ", output)})";
            };
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type.
        /// </summary>
        /// <param name="parameters">The parameters or argument expressions to mirror into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall(IEnumerable<IParameterSymbol> parameters)
        {
            var dict = new Dictionary<string, string>();
            foreach (var parameter in parameters ?? Array.Empty<IParameterSymbol>())
            {
                this.AddNamespaceImport(parameter.Type);
                dict.Add(parameter.Type.Name, parameter.Name);
            }
            return this.WithThisCall(dict);
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type.
        /// </summary>
        /// <param name="baseConstructor">The base constructor used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall(IMethodSymbol baseConstructor)
        {
            return this.WithThisCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor.
        /// </summary>
        /// <remarks>Use this overload when the base constructor takes no arguments.</remarks>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall()
        {
            this._baseCall = () => ": base()";
            return this;
        }

        /// <summary>
        /// Configures an explicit base-constructor call using the supplied argument expressions only.
        /// </summary>
        /// <param name="arguments">The argument expressions to pass to the emitted <c>base(...)</c> call.</param>
        /// <remarks>Prefer this overload when this constructor already declared its own parameters and you only need to control the forwarded arguments.</remarks>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCallArguments(params string[] arguments)
        {
            this._baseCall = () =>
            {
                return arguments is null || arguments.Length == 0 ? ": base()" : $": base({string.Join(", ", arguments)})";
            };

            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor while also declaring matching parameters on this constructor.
        /// </summary>
        /// <param name="parameters">The parameter-type-to-argument mapping to declare on this constructor and forward into the emitted <c>base(...)</c> call.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall(Dictionary<string, string> parameters)
        {
            foreach (var parameter in parameters)
            {
                this.AddParameter(parameter.Key, parameter.Value);
            }

            this._baseCall = () =>
            {
                var output = parameters.Select(x => x.Value);
                return $": base({string.Join(", ", output)})";
            };
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor.
        /// </summary>
        /// <param name="parameters">The parameters or argument expressions to mirror into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall(IEnumerable<IParameterSymbol> parameters)
        {
            var dict = new Dictionary<string, string>();
            foreach (var parameter in parameters ?? Array.Empty<IParameterSymbol>())
            {
                this.AddNamespaceImport(parameter.Type);
                dict.Add(parameter.Type.Name, parameter.Name);
            }
            return this.WithBaseCall(dict);
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor.
        /// </summary>
        /// <param name="baseConstructor">The base constructor used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall(IMethodSymbol baseConstructor)
        {
            return this.WithBaseCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="namespaceImport">The namespace import used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(string namespaceImport)
        {
            this.Class.AddNamespaceImport(namespaceImport);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(ISymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Adds constructor to the current generated constructor.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder AddConstructor(Accessibility? accessModifier = null)
        {
            return this.Class.AddConstructor(accessModifier);
        }

        internal override void Write(in CodeWriter writer)
        {
            if (this._xmlDoc is ParameterDocumentationComment parameterDocumentation)
            {
                parameterDocumentation.RemoveUnusedParameters(this._parameters);
            }

            this._xmlDoc?.Write(writer);

            foreach (var attribute in this._attributes)
            {
                writer.AppendLine($"[{attribute}]");
            }

            var modifier = this.AccessModifier switch
            {
                null => this.Class.AccessModifier.ToString().ToLowerInvariant(),
                _ => this.AccessModifier.Code()
            };
            var parameters = this._parameters.Any() ? string.Join(", ", this._parameters.Select(x => x.ToString())) : string.Empty;
            using (writer.Block($"{modifier} {this.Class.Name}({parameters})", this._baseCall.Invoke()))
            {
                this._methodBodyWriter?.Invoke(writer);
            }
        }
    }
}
