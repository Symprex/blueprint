using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter;

/// <summary>
/// Builds a generated delegate declaration, including parameters, return type, attributes, and XML documentation.
/// </summary>
public sealed class DelegateBuilder : BuilderBase<DelegateBuilder>, IParameterized<DelegateBuilder>
{
    private ParameterDocumentationComment? _xmlDoc;
    private readonly List<string> _attributes = new List<string>();
    private readonly GenericCollection _generics = new GenericCollection();
    private readonly List<ParameterBuilder<DelegateBuilder>> _parameters = new List<ParameterBuilder<DelegateBuilder>>();

    internal DelegateBuilder(string name, CodeBuilder builder)
    {
        this.Name = name;
        this.Builder = builder;
    }

    DelegateBuilder IParameterized<DelegateBuilder>.Parent => this;

    List<ParameterBuilder<DelegateBuilder>> IParameterized<DelegateBuilder>.Parameters => this._parameters;

    /// <summary>
    /// Gets the delegate name that will be emitted.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the CodeBuilder that owns this generated delegate.
    /// </summary>
    public CodeBuilder Builder { get; internal set; }

    /// <summary>
    /// Gets the accessibility that will be emitted for the generated delegate.
    /// </summary>
    public Accessibility? AccessModifier { get; private set; }

    /// <summary>
    /// Gets the return type that will be emitted for the generated delegate.
    /// </summary>
    public string? ReturnType { get; private set; }

    /// <summary>
    /// Sets the XML summary that will be emitted for the generated delegate.
    /// </summary>
    /// <param name="summary">The text to place inside the emitted summary documentation.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder WithSummary(string summary)
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
    /// Adds or replaces XML documentation for a parameter on the generated delegate.
    /// </summary>
    /// <param name="paramName">The name of the generated parameter whose XML documentation should be updated.</param>
    /// <param name="documentation">The text to emit inside the generated param XML element.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder WithParameterDoc(string paramName, string documentation)
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
    /// Adds a generic type parameter to the generated delegate and optionally configures its constraints.
    /// </summary>
    /// <param name="name">The generated name to emit.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder AddGeneric(string name)
    {
        return this.AddGeneric(name, _ => { });
    }

    /// <summary>
    /// Adds a generic type parameter to the generated delegate and optionally configures its constraints.
    /// </summary>
    /// <param name="name">The generated name to emit.</param>
    /// <param name="configureBuilder">A callback that can further configure the newly created builder before it is attached.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
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
    /// Sets the generated delegate to public accessibility.
    /// </summary>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder MakePublicDelegate()
    {
        return this.WithAccessModifier(Accessibility.Public);
    }

    /// <summary>
    /// Sets the generated delegate to internal accessibility.
    /// </summary>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder MakeInternalDelegate()
    {
        return this.WithAccessModifier(Accessibility.Internal);
    }

    /// <summary>
    /// Sets the accessibility emitted for the generated delegate.
    /// </summary>
    /// <param name="accessModifier">The accessibility modifier to emit.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder WithAccessModifier(Accessibility accessModifier)
    {
        this.AccessModifier = accessModifier;
        return this;
    }

    /// <summary>
    /// Adds an attribute to the generated delegate if it is not already present.
    /// </summary>
    /// <param name="attribute">The attribute to inspect or emit.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder AddAttribute(string attribute)
    {
        var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
        if (!this._attributes.Contains(sanitized))
        {
            this._attributes.Add(sanitized);
        }

        return this;
    }

    /// <summary>
    /// Queues an assembly-level attribute on the generated file that owns this delegate.
    /// </summary>
    /// <param name="attribute">The attribute to inspect or emit.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public override DelegateBuilder AddAssemblyAttribute(string attribute)
    {
        this.Builder.AddAssemblyAttribute(attribute);
        return this;
    }

    /// <summary>
    /// Registers a namespace import on the generated file that owns this delegate.
    /// </summary>
    /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public override DelegateBuilder AddNamespaceImport(string importedNamespace)
    {
        this.Builder.AddNamespaceImport(importedNamespace);
        return this;
    }

    /// <summary>
    /// Registers a namespace import on the generated file that owns this delegate.
    /// </summary>
    /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public override DelegateBuilder AddNamespaceImport(ISymbol symbol)
    {
        this.Builder.AddNamespaceImport(symbol);
        return this;
    }

    /// <summary>
    /// Registers a namespace import on the generated file that owns this delegate.
    /// </summary>
    /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public override DelegateBuilder AddNamespaceImport(INamespaceSymbol symbol)
    {
        this.Builder.AddNamespaceImport(symbol);
        return this;
    }

    /// <summary>
    /// Sets the return type emitted for the generated delegate.
    /// </summary>
    /// <param name="returnType">The return type used by this operation.</param>
    /// <returns>The same builder so fluent configuration can continue.</returns>
    public DelegateBuilder WithReturnType(string returnType)
    {
        this.ReturnType = returnType;
        return this;
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

        var parameters = this._parameters.Any() ? string.Join(", ", this._parameters.Select(x => x.ToString())) : string.Empty;
        var parts = new string?[]
        {
            this.AccessModifier.Code(),
            "delegate",
            this.ReturnType is null || string.IsNullOrEmpty(this.ReturnType) ? "void" : this.ReturnType.Trim(),
            $"{this.Name}{this._generics}({parameters})",
        }.Where(x => !string.IsNullOrEmpty(x));

        var output = string.Join(" ", parts);
        var constraints = this._generics.Contraints();
        if (constraints.Any())
        {
            writer.AppendLine(output);
            writer.IncreaseIndent();
            for (var i = 0; i < constraints.Length; i++)
            {
                var constraint = constraints[i];
                if (i + 1 == constraints.Length)
                {
                    constraint += ";";
                }

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
