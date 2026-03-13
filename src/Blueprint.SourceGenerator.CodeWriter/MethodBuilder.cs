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
    /// Builds method signatures, metadata, and emitted method bodies.
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
            Name = name;
            AccessModifier = accessModifier;
            Class = builder;
        }

        List<ParameterBuilder<MethodBuilder>> IParameterized<MethodBuilder>.Parameters => _parameters;
        MethodBuilder IParameterized<MethodBuilder>.Parent => this;

        public IReadOnlyCollection<ParameterBuilder<MethodBuilder>> Parameters => _parameters;

        public string Name { get; }

        public string? ReturnType { get; private set; }

        public bool IsAsync { get; private set; }

        public bool IsAbstract { get; private set; }

        public bool HasBody
        {
            get
            {
                if (_methodBodyWriter is null)
                    return false;

                var writer = new CodeWriter(IndentStyle.Spaces);
                _methodBodyWriter(writer);
                return !string.IsNullOrEmpty(writer.ToString());
            }
        }

        public ClassBuilder Class { get; }

        public Accessibility? AccessModifier { get; private set; }

        public bool IsStatic { get; private set; }

        /// <summary>
        /// Configures summary.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder WithSummary(string summary)
        {
            if (_xmlDoc is null || _xmlDoc is not SummaryDocumentationComment summaryDoc)
                _xmlDoc = new ParameterDocumentationComment { Summary = summary };
            else
                summaryDoc.Summary = summary;

            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder WithInheritDoc(string from)
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
        public MethodBuilder WithParameterDoc(string paramName, string documentation)
        {
            if (_xmlDoc is null)
                _xmlDoc = new ParameterDocumentationComment();

            if (_xmlDoc is not ParameterDocumentationComment parameterDoc)
                throw new Exception("DocumentationComment has already been initialized with a non ParameterDocumentationComment");

            parameterDoc.AddParameter(paramName, documentation);

            return this;
        }

        /// <summary>
        /// Adds generic.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder AddGeneric(string name) =>
            AddGeneric(name, _ => { });

        /// <summary>
        /// Adds generic.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="configureBuilder">The configure builder value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
        {
            if (configureBuilder is null)
                throw new ArgumentNullException(nameof(configureBuilder));
            else if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            else if (_generics.Any(x => x.Name == name))
                throw new ArgumentException($"The argument {name} already exists");

            var builder = new GenericBuilder(name);
            configureBuilder(builder);
            _generics.Add(builder);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public override MethodBuilder AddNamespaceImport(string importedNamespace)
        {
            Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override MethodBuilder AddNamespaceImport(ISymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override MethodBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public override MethodBuilder AddAssemblyAttribute(string attribute)
        {
            Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds property.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
        {
            return Class.AddProperty(name, accessModifier);
        }

        /// <summary>
        /// Marks async.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakeAsync()
        {
            IsAsync = true;
            return this;
        }

        /// <summary>
        /// Sets whether the generated method is abstract.
        /// </summary>
        /// <param name="isAbstract">The is abstract value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder Abstract(bool isAbstract = true)
        {
            IsAbstract = isAbstract;
            return this;
        }

        /// <summary>
        /// Marks public method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakePublicMethod() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Marks private method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakePrivateMethod() => WithAccessModifier(Accessibility.Private);

        /// <summary>
        /// Marks protected method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakeProtectedMethod() => WithAccessModifier(Accessibility.Protected);

        /// <summary>
        /// Marks internal method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakeInternalMethod() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Configures access modifier.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Sets whether the generated method is emitted with the override modifier.
        /// </summary>
        /// <param name="override">The override value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder Override(bool @override = true)
        {
            _override = @override;
            return this;
        }

        /// <summary>
        /// Marks static method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakeStaticMethod()
        {
            IsStatic = true;
            return this;
        }

        /// <summary>
        /// Marks virtual method.
        /// </summary>
        /// <returns>The generated result.</returns>
        public MethodBuilder MakeVirtualMethod()
        {
            _virtual = true;
            return this;
        }

        /// <summary>
        /// Adds attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder AddAttribute(string attribute)
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
        public MethodBuilder WithBody(Action<ICodeWriter> writerDelegate)
        {
            _methodBodyWriter = writerDelegate;
            return this;
        }

        /// <summary>
        /// Configures return type.
        /// </summary>
        /// <param name="returnType">The return type value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder WithReturnType(string returnType)
        {
            ReturnType = returnType;
            return this;
        }

        internal override void Write(in CodeWriter writer)
        {
            var output = ReturnType is null || string.IsNullOrEmpty(ReturnType) ? "void" : ReturnType.Trim();
            if (IsAsync)
                output = $"async {output}";

            if (_override)
                output = $"override {output}";
            else if (_virtual)
                output = $"virtual {output}";
            else if (IsAbstract)
                output = $"abstract {output}";
            else if (IsStatic)
                output = $"static {output}";

            var parameters = string.Join(", ", _parameters.Select(x => x.ToString()));
            output = $"{AccessModifier.Code()} {output} {Name}{_generics}({parameters})";

            if(_xmlDoc is ParameterDocumentationComment parameterDocumentation)
                parameterDocumentation.RemoveUnusedParameters(_parameters);

            _xmlDoc?.Write(writer);

            foreach (var attribute in _attributes)
                writer.AppendLine($"[{attribute}]");

            if (IsAbstract)
            {
                writer.AppendLine($"{output.Trim()};");
                return;
            }

            using (writer.Block(output.Trim(), _generics.Contraints()))
            {
                _methodBodyWriter?.Invoke(writer);
            }
        }
    }
}
