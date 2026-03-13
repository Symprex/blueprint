using System;
using System.Collections.Generic;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter;

/// <summary>
/// Represents delegate builder.
/// </summary>
public sealed class DelegateBuilder : BuilderBase<DelegateBuilder>, IParameterized<DelegateBuilder>
{
    private ParameterDocumentationComment? _xmlDoc;
    private readonly List<string> _attributes = new List<string>();
    private readonly GenericCollection _generics = new GenericCollection();
    private readonly List<ParameterBuilder<DelegateBuilder>> _parameters = new List<ParameterBuilder<DelegateBuilder>>();

    internal DelegateBuilder(string name, CodeBuilder builder)
    {
        Name = name;
        Builder = builder;
    }

    DelegateBuilder IParameterized<DelegateBuilder>.Parent => this;

    List<ParameterBuilder<DelegateBuilder>> IParameterized<DelegateBuilder>.Parameters => _parameters;

    public string Name { get; }

    public CodeBuilder Builder { get; internal set; }

    public Accessibility? AccessModifier { get; private set; }

    public string? ReturnType { get; private set; }

    /// <summary>
    /// Configures summary.
    /// </summary>
    /// <param name="summary">The summary value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder WithSummary(string summary)
    {
        if (_xmlDoc is null || _xmlDoc is not SummaryDocumentationComment summaryDoc)
            _xmlDoc = new ParameterDocumentationComment { Summary = summary };
        else
            summaryDoc.Summary = summary;

        return this;
    }

    /// <summary>
    /// Configures parameter doc.
    /// </summary>
    /// <param name="paramName">The param name value.</param>
    /// <param name="documentation">The documentation value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder WithParameterDoc(string paramName, string documentation)
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
    public DelegateBuilder AddGeneric(string name) =>
            AddGeneric(name, _ => { });

    /// <summary>
    /// Adds generic.
    /// </summary>
    /// <param name="name">The name value.</param>
    /// <param name="configureBuilder">The configure builder value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
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
    /// Marks public delegate.
    /// </summary>
    /// <returns>The generated result.</returns>
    public DelegateBuilder MakePublicDelegate() => WithAccessModifier(Accessibility.Public);

    /// <summary>
    /// Marks internal delegate.
    /// </summary>
    /// <returns>The generated result.</returns>
    public DelegateBuilder MakeInternalDelegate() => WithAccessModifier(Accessibility.Internal);

    /// <summary>
    /// Configures access modifier.
    /// </summary>
    /// <param name="accessModifier">The access modifier value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder WithAccessModifier(Accessibility accessModifier)
    {
        AccessModifier = accessModifier;
        return this;
    }

    /// <summary>
    /// Adds attribute.
    /// </summary>
    /// <param name="attribute">The attribute value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder AddAttribute(string attribute)
    {
        var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
        if (!_attributes.Contains(sanitized))
            _attributes.Add(sanitized);

        return this;
    }

    /// <summary>
    /// Adds assembly attribute.
    /// </summary>
    /// <param name="attribute">The attribute value.</param>
    /// <returns>The generated result.</returns>
    public override DelegateBuilder AddAssemblyAttribute(string attribute)
    {
        Builder.AddAssemblyAttribute(attribute);
        return this;
    }

    /// <summary>
    /// Adds namespace import.
    /// </summary>
    /// <param name="importedNamespace">The imported namespace value.</param>
    /// <returns>The generated result.</returns>
    public override DelegateBuilder AddNamespaceImport(string importedNamespace)
    {
        Builder.AddNamespaceImport(importedNamespace);
        return this;
    }

    /// <summary>
    /// Adds namespace import.
    /// </summary>
    /// <param name="symbol">The symbol value.</param>
    /// <returns>The generated result.</returns>
    public override DelegateBuilder AddNamespaceImport(ISymbol symbol)
    {
        Builder.AddNamespaceImport(symbol);
        return this;
    }

    /// <summary>
    /// Adds namespace import.
    /// </summary>
    /// <param name="symbol">The symbol value.</param>
    /// <returns>The generated result.</returns>
    public override DelegateBuilder AddNamespaceImport(INamespaceSymbol symbol)
    {
        Builder.AddNamespaceImport(symbol);
        return this;
    }

    /// <summary>
    /// Configures return type.
    /// </summary>
    /// <param name="returnType">The return type value.</param>
    /// <returns>The generated result.</returns>
    public DelegateBuilder WithReturnType(string returnType)
    {
        ReturnType = returnType;
        return this;
    }

    internal override void Write(in CodeWriter writer)
    {
        if (_xmlDoc is ParameterDocumentationComment parameterDocumentation)
            parameterDocumentation.RemoveUnusedParameters(_parameters);

        _xmlDoc?.Write(writer);

        foreach (var attribute in _attributes)
            writer.AppendLine($"[{attribute}]");

        var parameters = _parameters.Any() ? string.Join(", ", _parameters.Select(x => x.ToString())) : string.Empty;
        var parts = new string?[]
        {
            AccessModifier.Code(),
            "delegate",
            ReturnType is null || string.IsNullOrEmpty(ReturnType) ? "void" : ReturnType.Trim(),
            $"{Name}{_generics}({parameters})",
        }.Where(x => !string.IsNullOrEmpty(x));

        var output = string.Join(" ", parts);
        var constraints = _generics.Contraints();
        if(constraints.Any())
        {
            writer.AppendLine(output);
            writer.IncreaseIndent();
            for(int i = 0; i < constraints.Length; i++)
            {
                var constraint = constraints[i];
                if (i + 1 == constraints.Length)
                    constraint += ";";
                writer.AppendLine(constraint);
            }
            writer.DecreaseIndent();
        }
        else
        {
            writer.AppendLine(output + ";");
        }
    }
}
