using System.Linq;
using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Adds Roslyn source-generator helpers for rendering CodeWriter builders and adding the results to a generation context.
/// </summary>
public static class CodeHelpersGeneratorExtensions
{
    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this GeneratorExecutionContext context, CodeBuilder builder)
    {
        if (string.IsNullOrEmpty(builder.Namespace) || builder.Namespace is null)
            return;

        if(builder.Classes.Count == 1 && builder.Enums.Count == 0 && builder.Records.Count == 0)
        {
            context.AddSource(builder.Classes.First());
            return;
        }
        else if (builder.Classes.Count == 0 && builder.Enums.Count == 1 && builder.Records.Count == 0)
        {
            context.AddSource(builder.Enums.First());
            return;
        }
        else if (builder.Classes.Count == 0 && builder.Enums.Count == 0 && builder.Records.Count == 1)
        {
            context.AddSource(builder.Records.First());
            return;
        }

        foreach (var @class in builder.Classes)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddClass(@class));
        }

        foreach (var @enum in builder.Enums)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddEnum(@enum));
        }

        foreach (var @record in builder.Records)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddRecord(@record));
        }
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this GeneratorExecutionContext context, ClassBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this GeneratorExecutionContext context, EnumBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this GeneratorExecutionContext context, RecordBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this SourceProductionContext context, ClassBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this SourceProductionContext context, EnumBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder and adds the generated source to the Roslyn generation context.
    /// </summary>
    /// <param name="context">The Roslyn generation context that should receive the generated source.</param>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    public static void AddSource(this SourceProductionContext context, RecordBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Renders the supplied builder to SourceText, applying Roslyn formatting options when available.
    /// </summary>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    /// <param name="options">The parse options used to normalize and format the generated source.</param>
    /// <returns>The rendered source text ready to pass to Roslyn.</returns>
    public static SourceText Build(this CodeBuilder builder, ParseOptions options)
    {
        return SourceText(builder, options);
    }

    /// <summary>
    /// Renders the supplied builder to SourceText, applying Roslyn formatting options when available.
    /// </summary>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    /// <param name="options">The parse options used to normalize and format the generated source.</param>
    /// <returns>The rendered source text ready to pass to Roslyn.</returns>
    public static SourceText Build(this ClassBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    /// <summary>
    /// Renders the supplied builder to SourceText, applying Roslyn formatting options when available.
    /// </summary>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    /// <param name="options">The parse options used to normalize and format the generated source.</param>
    /// <returns>The rendered source text ready to pass to Roslyn.</returns>
    public static SourceText Build(this EnumBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    /// <summary>
    /// Renders the supplied builder to SourceText, applying Roslyn formatting options when available.
    /// </summary>
    /// <param name="builder">The builder whose output should be rendered or added to the Roslyn context.</param>
    /// <param name="options">The parse options used to normalize and format the generated source.</param>
    /// <returns>The rendered source text ready to pass to Roslyn.</returns>
    public static SourceText Build(this RecordBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    private static SourceText SourceText(CodeBuilder builder, ParseOptions? options)
    {
        if (options is CSharpParseOptions parseOptions)
        {
            ConfigureFromOptions(builder, parseOptions);

            var source = builder.Build();
            var syntaxTree = CSharpSyntaxTree.ParseText(Text.SourceText.From(source, Encoding.UTF8), parseOptions);
            var formattedRoot = (CSharpSyntaxNode)syntaxTree.GetRoot().NormalizeWhitespace();
            return Text.SourceText.From(CSharpSyntaxTree.Create(formattedRoot).ToString(), Encoding.UTF8);
        }

        return Text.SourceText.From(builder.Build(), Encoding.UTF8);
    }

    private static void ConfigureFromOptions(CodeBuilder builder, CSharpParseOptions options)
    {
        if (options.SpecifiedLanguageVersion >= LanguageVersion.CSharp10)
        {
            builder.TopLevelNamespace();
        }
    }
}
