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
    /// Builds constructor signatures, base calls, and constructor bodies.
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

        public IReadOnlyCollection<ParameterBuilder<ConstructorBuilder>> Parameters => _parameters;

        public Accessibility? AccessModifier { get; }

        public ClassBuilder Class { get; }

        internal int Count => _parameters.Count;

        /// <summary>
        /// Configures summary.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithSummary(string summary)
        {
            if (_xmlDoc is not null && _xmlDoc is SummaryDocumentationComment summaryComment)
                summaryComment.Summary = summary;
            else
                _xmlDoc = new ParameterDocumentationComment { Summary = summary };

            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Configures parameter doc.
        /// </summary>
        /// <param name="paramName">The param name value.</param>
        /// <param name="documentation">The documentation value.</param>
        /// <returns>The generated result.</returns>
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
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public override ConstructorBuilder AddAssemblyAttribute(string attribute)
        {
            Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Configures body.
        /// </summary>
        /// <param name="writerDelegate">The writer delegate value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithBody(Action<ICodeWriter> writerDelegate)
        {
            _methodBodyWriter = writerDelegate;
            return this;
        }

        /// <summary>
        /// Configures this call.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithThisCall()
        {
            _baseCall = () => ": this()";
            return this;
        }

        /// <summary>
        /// Configures this call.
        /// </summary>
        /// <param name="parameters">The parameters value.</param>
        /// <returns>The generated result.</returns>
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
        /// Configures this call.
        /// </summary>
        /// <param name="parameters">The parameters value.</param>
        /// <returns>The generated result.</returns>
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
        /// Configures this call.
        /// </summary>
        /// <param name="baseConstructor">The base constructor value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithThisCall(IMethodSymbol baseConstructor)
        {
            return WithThisCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Configures base call.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithBaseCall()
        {
            _baseCall = () => ": base()";
            return this;
        }

        /// <summary>
        /// Configures base call arguments.
        /// </summary>
        /// <param name="arguments">The arguments value.</param>
        /// <returns>The generated result.</returns>
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
        /// Configures base call.
        /// </summary>
        /// <param name="parameters">The parameters value.</param>
        /// <returns>The generated result.</returns>
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
        /// Configures base call.
        /// </summary>
        /// <param name="parameters">The parameters value.</param>
        /// <returns>The generated result.</returns>
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
        /// Configures base call.
        /// </summary>
        /// <param name="baseConstructor">The base constructor value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder WithBaseCall(IMethodSymbol baseConstructor)
        {
            return WithBaseCall(baseConstructor.Parameters);
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="namespaceImport">The namespace import value.</param>
        /// <returns>The generated result.</returns>
        public override ConstructorBuilder AddNamespaceImport(string namespaceImport)
        {
            Class.AddNamespaceImport(namespaceImport);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override ConstructorBuilder AddNamespaceImport(ISymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override ConstructorBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Adds constructor.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
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
