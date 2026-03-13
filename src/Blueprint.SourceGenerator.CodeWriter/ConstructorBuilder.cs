using System;
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
            AccessModifier = accessModifier;
            Class = classBuilder;
            _baseCall = () => string.Empty;
        }

        List<ParameterBuilder<ConstructorBuilder>> IParameterized<ConstructorBuilder>.Parameters => _parameters;

        ConstructorBuilder IParameterized<ConstructorBuilder>.Parent => this;

        /// <summary>
        /// Gets the parameters currently configured for the generated constructor signature.
        /// </summary>
        public IReadOnlyCollection<ParameterBuilder<ConstructorBuilder>> Parameters => _parameters;

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated constructor.
        /// </summary>
        public Accessibility? AccessModifier { get; }

        /// <summary>
        /// Gets the ClassBuilder that owns this generated constructor.
        /// </summary>
        public ClassBuilder Class { get; }

        internal int Count => _parameters.Count;

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated constructor.
        /// </summary>
        /// <param name="summary">The caller-facing description of why the generated constructor exists or what it initializes.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithSummary(string summary)
        {
            if (_xmlDoc is not null && _xmlDoc is SummaryDocumentationComment summaryComment)
                summaryComment.Summary = summary;
            else
                _xmlDoc = new ParameterDocumentationComment { Summary = summary };

            return this;
        }

        /// <summary>
        /// Configures the generated constructor to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated constructor. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
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
            if (_xmlDoc is not null && _xmlDoc is not ParameterDocumentationComment)
                throw new Exception("Documentation Comment has already been initialized using an InheritDoc.");

            var parameterDoc = _xmlDoc as ParameterDocumentationComment;
            if(parameterDoc is not null)
                parameterDoc.AddParameter(paramName, documentation);

            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this constructor.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddAssemblyAttribute(string attribute)
        {
            Class.AddAssemblyAttribute(attribute);
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
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated constructor body.
        /// </summary>
        /// <param name="writerDelegate">A callback that writes the generated constructor body, typically by using <see cref="ICodeWriterExtensions"/> helpers to express statements and control flow.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBody(Action<ICodeWriter> writerDelegate)
        {
            _methodBodyWriter = writerDelegate;
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type.
        /// </summary>
        /// <remarks>Use this overload when the target constructor takes no arguments.</remarks>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall()
        {
            _baseCall = () => ": this()";
            return this;
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type while also declaring matching parameters on this constructor.
        /// </summary>
        /// <param name="parameters">The parameter-type-to-argument mapping to declare on this constructor and forward into the emitted <c>this(...)</c> call.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall(Dictionary<string, string> parameters)
        {
            foreach(var parameter in parameters)
            {
                this.AddParameter(parameter.Key, parameter.Value);
            }

            _baseCall = () =>
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
                AddNamespaceImport(parameter.Type);
                dict.Add(parameter.Type.Name, parameter.Name);
            }
            return WithThisCall(dict);
        }

        /// <summary>
        /// Configures the generated constructor to chain to another constructor on the same type.
        /// </summary>
        /// <param name="baseConstructor">The base constructor used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithThisCall(IMethodSymbol baseConstructor)
        {
            return WithThisCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor.
        /// </summary>
        /// <remarks>Use this overload when the base constructor takes no arguments.</remarks>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall()
        {
            _baseCall = () => ": base()";
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
            _baseCall = () =>
            {
                if (arguments is null || arguments.Length == 0)
                {
                    return ": base()";
                }

                return $": base({string.Join(", ", arguments)})";
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

            _baseCall = () =>
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
                AddNamespaceImport(parameter.Type);
                dict.Add(parameter.Type.Name, parameter.Name);
            }
            return WithBaseCall(dict);
        }

        /// <summary>
        /// Configures the generated constructor to chain to a base-type constructor.
        /// </summary>
        /// <param name="baseConstructor">The base constructor used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder WithBaseCall(IMethodSymbol baseConstructor)
        {
            return WithBaseCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="namespaceImport">The namespace import used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(string namespaceImport)
        {
            Class.AddNamespaceImport(namespaceImport);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(ISymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this constructor.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ConstructorBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Adds constructor to the current generated constructor.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ConstructorBuilder AddConstructor(Accessibility? accessModifier = null)
        {
            return Class.AddConstructor(accessModifier);
        }

        internal override void Write(in CodeWriter writer)
        {
            if(_xmlDoc is ParameterDocumentationComment parameterDocumentation)
                parameterDocumentation.RemoveUnusedParameters(_parameters);

            _xmlDoc?.Write(writer);

            foreach (var attribute in _attributes)
                writer.AppendLine($"[{attribute}]");

            var modifier = AccessModifier switch
            {
                null => Class.AccessModifier.ToString().ToLowerInvariant(),
                _ => AccessModifier.Code()
            };
            var parameters = _parameters.Any() ? string.Join(", ", _parameters.Select(x => x.ToString())) : string.Empty;
            using(writer.Block($"{modifier} {Class.Name}({parameters})", _baseCall.Invoke()))
            {
                _methodBodyWriter?.Invoke(writer);
            }
        }
    }
}
